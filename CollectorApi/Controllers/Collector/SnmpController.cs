using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CollectorApi.WebSocket;
using Newtonsoft.Json;
using CollectorApi.Tool;
using static CollectorApi.WebSocket.ApiWebSocketHub;
using System.Diagnostics;
using Neo4j.Driver;
using Neo4jClient;
using System.Net;
using System.Net.Sockets;
using static CollectorApi.Controllers.Model.Common;
using Microsoft.AspNetCore.Cors;
using InfluxDB.Client.Writes;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;
using System.Text;

namespace CollectorApi.Controllers.Collector
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("CorsPolicy")]
    public class SnmpController : BaseApiController
    {
        private readonly IConfiguration _configuration;
        private readonly IHubContext<ApiWebSocketHub> _hubContext;
        // private string neu4jUri = "neo4j+s://8a78992b.databases.neo4j.io";
        // private string neu4jUser = "neo4j";
        // private string neu4jPwd = "12345678";
        private string? neu4jUri;
        private string? neu4jUser;
        private string? neu4jPwd;
        private string? influxDbToken;
        private string? influxDbUrl;
        private string? influxDbOrg;
        private string? influxSnmpDbName;
        private string? PostgreDbNetmatrix;

        public SnmpController(IHubContext<ApiWebSocketHub> hubContext, IConfiguration configuration)
        {
            _hubContext = hubContext;
            _configuration = configuration;

            var influxDBSetting = _configuration.GetSection("InfluxDB");
            influxDbToken = influxDBSetting["Token"];
            influxDbUrl = influxDBSetting["Url"];
            influxDbOrg = influxDBSetting["Organization"];
            influxSnmpDbName = influxDBSetting["SnmpDbName"];

            var neo4jDBSetting = _configuration.GetSection("neo4jDB");
            neu4jUri = neo4jDBSetting["Url"];
            neu4jUser = neo4jDBSetting["Account"];
            neu4jPwd = neo4jDBSetting["Password"];

            var postgreDBSetting = _configuration.GetSection("PostgreDB");
            PostgreDbNetmatrix = postgreDBSetting["Netmatrix"];
        }

        [AllowAnonymous]
        [HttpGet("testApi")]
        public async Task<object> testApi()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                for (int i = 0; i < 99999; i++)
                {
                    sb.Append("a");
                }

                var obj = new
                {
                    test = sb.ToString()
                };

                apiResult.ResultSet = obj;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                apiResult.RetCode = -1;
            }


            // string mySettingValue = _configuration["MySetting"];

            // apiResult.ResultSet = mySettingValue;

            AfterProcess();
            return apiResult;
        }

        [HttpPost("GetSnmpLog")]
        public async Task<object> GetSnmpLog(ReqGetSnmpLog request)
        {
            try
            {
                using (var client = new InfluxDBClient(influxDbUrl, token: influxDbToken))
                {
                    string fluxParam = string.Empty;

                    if (!string.IsNullOrEmpty(request.DeviceIp))
                    {
                        fluxParam += $@" |> filter(fn: (r) => r.DeviceIp == ""{request.DeviceIp}"") ";
                    }

                    var fluxUsed = $@"from(bucket: ""crawlerDB"")
                                        |> range(start: -1d)
                                        |> filter(fn: (r) => r._measurement == ""snmpLog"")
                                        {fluxParam}
                                        |> keep(columns: [""_time"", ""_field"", ""_value"", ""DeviceIp"", ""Used"", ""SensorNo""])
                                        |> pivot(rowKey: [""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                                        |> map(fn: (r) => ({{ r with Used: if exists r.Used then r.Used else 0.0 }}))
                                        |> aggregateWindow(every: 5m, fn: max, column: ""Used"", createEmpty: false)
                                        |> pivot(rowKey: [""_time"", ""DeviceIp""], columnKey: [""SensorNo""], valueColumn: ""Used"")";

                    var fluxTotal = $@"from(bucket: ""crawlerDB"")
                                        |> range(start: -1d)
                                        |> filter(fn: (r) => r._measurement == ""snmpLog"")
                                        {fluxParam}
                                        |> keep(columns: [""_time"", ""_field"", ""_value"", ""DeviceIp"", ""Total"", ""SensorNo""])
                                        |> pivot(rowKey: [""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                                        |> map(fn: (r) => ({{ r with Total: if exists r.Total then r.Total else 0.0 }}))
                                        |> aggregateWindow(every: 5m, fn: max, column: ""Total"", createEmpty: false)
                                        |> pivot(rowKey: [""_time"", ""DeviceIp""], columnKey: [""SensorNo""], valueColumn: ""Total"")";

                    var fluxUseTask = client.GetQueryApi().QueryAsync(fluxUsed, influxDbOrg);
                    var fluxTotalTask = client.GetQueryApi().QueryAsync(fluxTotal, influxDbOrg);
                    var fluxUsedTables = await fluxUseTask.ConfigureAwait(false);
                    var fluxTotalTables = await fluxTotalTask.ConfigureAwait(false);

                    // DB 資料轉為物件
                    List<Dictionary<string, object>> UsedList = new List<Dictionary<string, object>>();
                    foreach (var used in fluxUsedTables)
                    {
                        foreach (var fluxRecord in used.Records)
                        {
                            var dictionary = new Dictionary<string, object>();
                            foreach (var data in fluxRecord.Values)
                            {
                                if (data.Key == "_time")
                                {
                                    if (DateTime.TryParse(data.Value.ToString(), out DateTime time))
                                    {
                                        dictionary["Tdate"] = time;
                                    }
                                }
                                else if (int.TryParse(data.Key, out var value))
                                {
                                    dictionary[$"Used{value}"] = data.Value;
                                }
                                else
                                {
                                    dictionary[data.Key] = data.Value;
                                }
                            }
                            UsedList.Add(dictionary);
                        }
                    }

                    List<Dictionary<string, object>> TotalList = new List<Dictionary<string, object>>();
                    foreach (var total in fluxTotalTables)
                    {
                        foreach (var fluxRecord in total.Records)
                        {
                            var dictionary = new Dictionary<string, object>();
                            foreach (var data in fluxRecord.Values)
                            {
                                if (data.Key == "_time")
                                {
                                    if (DateTime.TryParse(data.Value.ToString(), out DateTime time))
                                    {
                                        dictionary["Tdate"] = time;
                                    }
                                }
                                else if (int.TryParse(data.Key, out var value))
                                {
                                    dictionary[$"Total{value}"] = data.Value;
                                }
                                else
                                {
                                    dictionary[data.Key] = data.Value;
                                }
                            }
                            TotalList.Add(dictionary);
                        }
                    }

                    // 合併資料
                    foreach (var used in UsedList)
                    {
                        var usedKey = (DateTime)used["Tdate"];

                        var total = TotalList.First(x => (DateTime)x["Tdate"] == usedKey);

                        foreach (var totalData in total)
                        {
                            if (!used.ContainsKey(totalData.Key))
                            {
                                used[totalData.Key] = totalData.Value;
                            }
                        }
                    }

                    apiResult.RetCode = 200;
                    apiResult.RetNumber = UsedList.Count;
                    apiResult.ResultSet = UsedList;
                }
            }
            catch (Exception ex)
            {
                apiResult.RetCode = -1;
                apiResult.RetMsg = ex.Message;
                logger.Error(ex);
            }

            AfterProcess();
            return apiResult;
        }

        [HttpGet("GetDevice")]
        public async Task<object> GetDevice()
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    // 查詢 Device 節點及其相關的 DeviceInterface 節點
                    var devices = await client.Cypher
                                              .Match("(d:snmpDevice)")
                                              .OptionalMatch("(d)-[ri:HAS_DEVICEINTERFACE]->(di:snmpDeviceInterface)")
                                              .With("d, collect(distinct di) as dis")
                                              .OptionalMatch("(d)-[rs:HAS_DEVICESTORAGE]->(ds:snmpDeviceStorage)")
                                              .Return((d, dis, ds) => new
                                              {
                                                  Device = d.As<neo4jSnmpTaskInfo>(),
                                                  DeviceInterface = dis.As<IEnumerable<neo4jOidReturnModule>>(),
                                                  DeviceStorage = ds.CollectAs<neo4jOidReturnModule>(),
                                              })
                                      .ResultsAsync.ConfigureAwait(false);

                    // 將 Device 和其 Interfaces 整合到 Device 對象中
                    var deviceList = devices.GroupBy(d => d.Device)
                                            .Select(g => new neo4jSnmpTaskInfo
                                            {
                                                DeviceIp = g.Key.DeviceIp,
                                                SnmpPort = g.Key.SnmpPort,
                                                Community = g.Key.Community,
                                                HostName = g.Key.HostName,
                                                SysDescr = g.Key.SysDescr,
                                                InterfaceList = g.SelectMany(x => x.DeviceInterface).ToList(),
                                                StorageList = g.SelectMany(x => x.DeviceStorage).ToList()
                                            })
                                            .ToList();

                    apiResult.RetNumber = deviceList.Count();
                    apiResult.ResultSet = deviceList;
                    apiResult.RetCode = 200;
                }

            }
            catch (Exception ex)
            {
                apiResult.RetCode = -1;
                apiResult.RetMsg = ex.ToString();
                logger.Error(ex);
            }

            AfterProcess();
            return apiResult;
        }

        [AllowAnonymous]
        [HttpPost("ReceiveSnmpLog")]
        public async Task<object> ReceiveSnmpLog(SendDataModel request)
        {
            if (CurrClients.TryGetValue(request.CollectorToken, out ClientInfo clientInfo))
            {
                foreach (var snmpLog in request.SnmpLogDataList)
                {
                    if (clientInfo.Mission.TryGetValue(snmpLog.MissionId, out Mission mission))
                    {
                        mission.ResponseDataList.Add(snmpLog);
                    }
                }

                apiResult.RetCode = 200;
            }
            else
            {
                apiResult.RetCode = 201;
            }

            return apiResult;
        }

        /// <summary>
        /// snmplog 寫入 DB
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("AddSnmpLog")]
        public async Task<object> AddSnmpLog(List<SnmpTaskInfo> request)
        {
            try
            {
                var inNeo4jDeviceList = JsonConvert.DeserializeObject<List<neo4jSnmpTaskInfo>>(JsonConvert.SerializeObject(request));

                if (inNeo4jDeviceList.Count > 0)
                {
                    List<Task> taskList = new List<Task>();

                    /*
                    taskList.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // BUG: 記憶體使用量會持續增長
                            using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                            {
                                await client.ConnectAsync().ConfigureAwait(false);

                                var query = client.Cypher;

                                query = query.Unwind(inNeo4jDeviceList, "taskInfo")
                                            .Merge("(device:snmpDevice {DeviceIp: taskInfo.DeviceIp})")
                                                .OnCreate()
                                                .Set("device.CrtDate = datetime({timezone: '+08:00'})")
                                                .OnMatch()
                                                .Set("device.UpdDate = datetime({timezone: '+08:00'})")
                                                .Set("device.DeviceIp = taskInfo.DeviceIp, device.SnmpPort = taskInfo.SnmpPort")
                                                .Set("device.Community = taskInfo.Community, device.HostName = taskInfo.HostName")
                                                .Set("device.SysDescr = taskInfo.SysDescr")
                                            .With("device, taskInfo")
                                            .OptionalMatch("(device)-[rInterface:HAS_DEVICEINTERFACE]->(oldIntf:snmpDeviceInterface)")
                                            .With("device, taskInfo, oldIntf, count((oldIntf)--()) as degree")
                                            .Where("degree = 1")// 不刪除有額外關聯的 Interface
                                            .DetachDelete("oldIntf")
                                            .With("device, taskInfo")
                                            .OptionalMatch("(device)-[rStorage:HAS_DEVICESTORAGE]->(oldStor:snmpDeviceStorage)")
                                            .DetachDelete("oldStor")
                                            .With("device, taskInfo")
                                            .ForEach(@"(interface IN taskInfo.InterfaceList |
                                        MERGE (intf:snmpDeviceInterface {Oid: interface.Oid, DeviceIp: taskInfo.DeviceIp, HostName: taskInfo.HostName, SysDescr: taskInfo.SysDescr})
                                        ON CREATE SET intf = interface, intf.DeviceIp = taskInfo.DeviceIp, intf.HostName = taskInfo.HostName, intf.SysDescr = taskInfo.SysDescr, intf.CrtDate = datetime({timezone: '+08:00'})
                                        MERGE (device)-[:HAS_DEVICEINTERFACE]->(intf))")
                                            .ForEach(@"(storage IN taskInfo.StorageList |
                                        MERGE (stor:snmpDeviceStorage {Oid: storage.Oid, DeviceIp: taskInfo.DeviceIp, HostName: taskInfo.HostName, SysDescr: taskInfo.SysDescr})
                                        ON CREATE SET stor = storage, stor.DeviceIp = taskInfo.DeviceIp, stor.HostName = taskInfo.HostName, stor.SysDescr = taskInfo.SysDescr, stor.CrtDate = datetime({timezone: '+08:00'})
                                        MERGE (device)-[:HAS_DEVICESTORAGE]->(stor))");

                                await query.ExecuteWithoutResultsAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex);
                        }
                    }));
                    */

                    taskList.Add(Task.Run(async () =>
                    {
                        List<SnmpLog> snmpLogList = new List<SnmpLog>();

                        foreach (var deviceData in request)
                        {
                            // CPU
                            {
                                var dataList = deviceData.snmpLogList.Where(x => x.SensorNo == 10).ToList();
                                foreach (var data in dataList)
                                {
                                    if (data.Used.HasValue)
                                    {
                                        data.Used = Math.Round(data.Used.Value, 0);
                                    }
                                    snmpLogList.Add(data);
                                }
                            }

                            // 取無線或有線網路第一筆的流量 (暫時寫死，因 DB 無資料關聯)
                            if (deviceData.InterfaceList != null)
                            {
                                deviceData.InterfaceList = deviceData.InterfaceList.Where(x => x.InterfaceStatus == 1).ToList();
                                int topWifiIndex = deviceData.InterfaceList.FindIndex(x => x.InterfaceType == 71);
                                int topEthernetIndex = deviceData.InterfaceList.FindIndex(x => x.InterfaceType == 6);

                                OidReturnModule? interfaceData = null;
                                if (topWifiIndex != -1)
                                {
                                    interfaceData = deviceData.InterfaceList[topWifiIndex];
                                }
                                else if (topEthernetIndex != -1)
                                {
                                    interfaceData = deviceData.InterfaceList[topEthernetIndex];
                                }
                                else
                                {
                                    continue;
                                }

                                // in 流量
                                {
                                    SnmpLog snmpLog = new SnmpLog();
                                    snmpLog.Tdate = interfaceData.Tdate.HasValue ? interfaceData.Tdate.Value.LocalDateTime : null;
                                    snmpLog.DeviceIp = deviceData.DeviceIp;
                                    snmpLog.SensorNo = 40;
                                    snmpLog.Used = interfaceData.IfInbps.HasValue ? Math.Round(interfaceData.IfInbps.Value, 0) : interfaceData.IfInbps;
                                    snmpLogList.Add(snmpLog);
                                }

                                // out 流量
                                {
                                    SnmpLog snmpLog = new SnmpLog();
                                    snmpLog.Tdate = interfaceData.Tdate.HasValue ? interfaceData.Tdate.Value.LocalDateTime : null;
                                    snmpLog.DeviceIp = deviceData.DeviceIp;
                                    snmpLog.SensorNo = 45;
                                    snmpLog.Used = interfaceData.IfOutbps.HasValue ? Math.Round(interfaceData.IfOutbps.Value, 0) : interfaceData.IfOutbps;
                                    snmpLogList.Add(snmpLog);
                                }
                            }

                            // 取第一筆硬碟、實體記憶體使用量、總量
                            if (deviceData.StorageList != null)
                            {
                                int topRamIndex = deviceData.StorageList.FindIndex(x => x.Total.HasValue && x.Total > 0 && x.OidName.ToLower().Contains("memory"));
                                int topDiskIndex = deviceData.StorageList.FindIndex(x => x.Total.HasValue && x.Total > 0 && !x.OidName.ToLower().Contains("memory"));

                                if (topRamIndex != -1)
                                {
                                    OidReturnModule ramData = deviceData.StorageList[topRamIndex];
                                    // disk
                                    {
                                        SnmpLog snmpLog = new SnmpLog();
                                        snmpLog.Tdate = ramData.Tdate.HasValue ? ramData.Tdate.Value.LocalDateTime : null;
                                        snmpLog.DeviceIp = deviceData.DeviceIp;
                                        snmpLog.SensorNo = 20;
                                        snmpLog.Used = ramData.Used.HasValue ? Math.Round(ramData.Used.Value, 0) : ramData.Used;
                                        snmpLog.Total = ramData.Total.HasValue ? Math.Round(ramData.Total.Value, 0) : ramData.Total;
                                        snmpLogList.Add(snmpLog);
                                    }
                                }
                                if (topDiskIndex != -1)
                                {
                                    OidReturnModule diskData = deviceData.StorageList[topDiskIndex];
                                    // disk
                                    {
                                        SnmpLog snmpLog = new SnmpLog();
                                        snmpLog.Tdate = diskData.Tdate.HasValue ? diskData.Tdate.Value.LocalDateTime : null;
                                        snmpLog.DeviceIp = deviceData.DeviceIp;
                                        snmpLog.SensorNo = 30;
                                        snmpLog.Used = diskData.Used.HasValue ? Math.Round(diskData.Used.Value, 0) : diskData.Used;
                                        snmpLog.Total = diskData.Total.HasValue ? Math.Round(diskData.Total.Value, 0) : diskData.Total;
                                        snmpLogList.Add(snmpLog);
                                    }
                                }
                            }
                        }
                        if (snmpLogList.Count > 0)
                        {
                            using (var client = new InfluxDBClient(influxDbUrl, token: influxDbToken))
                            {
                                using (var writeApi = client.GetWriteApi())
                                {
                                    List<PointData> dataList = new List<PointData>();

                                    foreach (var snmpLog in snmpLogList)
                                    {
                                        snmpLog.Status = snmpLog.Used.HasValue ? 80 : 90;

                                        var point = PointData
                                            .Measurement("snmpLog")
                                            .Tag("DeviceIp", snmpLog.DeviceIp)
                                            .Tag("SensorNo", snmpLog.SensorNo.ToString())
                                            .Field("Used", snmpLog.Used)
                                            .Field("Total", snmpLog.Total)
                                            .Field("Status", snmpLog.Status)
                                            .Timestamp(snmpLog.Tdate.Value, WritePrecision.Ns);

                                        dataList.Add(point);
                                    }

                                    writeApi.WritePoints(points: dataList, bucket: influxSnmpDbName, org: influxDbOrg);
                                }
                            }
                        }
                    }));

                    // 等待完成
                    Task t = Task.WhenAll(taskList);
                    await t.ConfigureAwait(false);
                }

                apiResult.RetCode = 200;
            }
            catch (Exception ex)
            {
                apiResult.RetMsg = ex.GetBaseException().Message;
                apiResult.RetCode = -1;
                logger.Error(ex);
            }
            finally
            {
                AfterProcess();
            }


            return apiResult;
        }

        [HttpPost("ScanSnmpDevice")]
        public async Task<object> ScanSnmpDevice(ScanDeviceRequest request)
        {
            var networkSegmentIpList = tool.ParseCidr(request.ScanCidr).ToList();
            if (networkSegmentIpList.Count > 0)
            {
                networkSegmentIpList.RemoveAt(0);
                networkSegmentIpList.RemoveAt(networkSegmentIpList.Count - 1);
            }
            List<IpStatus> networkSegmentIpList2 = new List<IpStatus>();
            foreach (var ip in networkSegmentIpList)
            {
                IpStatus ipStatus = new IpStatus();
                ipStatus.IP = ip;
                ipStatus.Status = false;
                networkSegmentIpList2.Add(ipStatus);
            }

            //解析得到的 IP List
            List<string> ipList = new List<string>();

            List<DeviceServiceModule> missionDetailList = test.CreateSensorList();

            List<SnmpTaskInfoMission> missionList = new List<SnmpTaskInfoMission>();

            // Collector 任務是否完成
            bool isMissionFinish = false;

            var missionId = Guid.NewGuid().ToString();

            try
            {
                if (!string.IsNullOrEmpty(request.StartIp) && !string.IsNullOrEmpty(request.EndIp))
                {
                    // 解析 IP 範圍
                    ipList = tool.ParseIpRange(request.StartIp, request.EndIp);

                    foreach (string ip in ipList)
                    {
                        SnmpTaskInfoMission missionData = new SnmpTaskInfoMission
                        {
                            MissionId = missionId,
                            DeviceIp = ip,
                            SnmpPort = request.Port,
                            Community = request.Community,
                            ServiceList = missionDetailList,
                            GetTraffic = request.GetTraffic,
                            ScanCidr = request.ScanCidr
                        };

                        missionList.Add(missionData);
                    }
                }
                else
                {
                    SnmpTaskInfoMission missionData = new SnmpTaskInfoMission
                    {
                        MissionId = missionId,
                        DeviceIp = string.Empty,
                        SnmpPort = request.Port,
                        Community = request.Community,
                        ServiceList = missionDetailList,
                        GetTraffic = request.GetTraffic,
                        ScanCidr = request.ScanCidr
                    };

                    missionList.Add(missionData);
                }

                // 新增任務
                Mission newMission = new Mission
                {
                    Status = 80,
                    CrtDate = DateTime.Now,
                };

                // 透過 Socket 連線下傳任務
                if (CurrClients.TryGetValue("Collector", out ClientInfo clientinfo))
                {
                    clientinfo.Mission.TryAdd(missionId, newMission);

                    string jMissionList = JsonConvert.SerializeObject(missionList);

                    _hubContext.Clients.All.SendAsync("ScanDevice", jMissionList);

                    // 5 分鐘內等待 Collector 回應
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    while (sw.ElapsedMilliseconds < 300 * 1000)
                    {
                        if (newMission.Status == 90)
                        {
                            isMissionFinish = true;
                            break;
                        }

                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    sw.Stop();
                }

                // 取出任務資料
                clientinfo.Mission.TryRemove(missionId, out newMission);

                if (isMissionFinish)
                {
                    apiResult.RetCode = 200;
                }
                else
                {
                    apiResult.RetMsg = "Collector timeout";
                    apiResult.RetCode = -1;
                    return apiResult;
                }

                var dbDataList = JsonConvert.DeserializeObject<List<SnmpTaskInfo>>(JsonConvert.SerializeObject(newMission.ResponseDataList));

                // 排序
                if (dbDataList != null)
                {
                    dbDataList = dbDataList.OrderBy(x => x.DeviceIp).ToList();
                }

                foreach (var dbData in dbDataList)
                {
                    // 區分 disk, ram
                    var cacheMemoryList = dbData.StorageList.Where(x => x.OidName.ToLower().Contains("cache")
                                                                    && x.OidName.ToLower().Contains("memory")).ToList();

                    var diskList = dbData.StorageList.Where(x => x.OidName != null && (x.OidName.IndexOf("Physical", StringComparison.OrdinalIgnoreCase) == -1
                                                                                        && x.OidName.IndexOf("Real", StringComparison.OrdinalIgnoreCase) == -1
                                                                                        && x.OidName.IndexOf("memory", StringComparison.OrdinalIgnoreCase) == -1
                                                                                        && x.OidName.IndexOf("Swap space", StringComparison.OrdinalIgnoreCase) == -1)).ToList();
                    diskList = JsonConvert.DeserializeObject<List<OidReturnModule>>(JsonConvert.SerializeObject(diskList));
                    foreach (var disk in diskList)
                    {
                        disk.OidType = 30;
                    }
                    dbData.diskList = diskList;

                    if (dbData.StorageList.Where(x => x.OidName != null && x.OidName.IndexOf("Physical", StringComparison.OrdinalIgnoreCase) != -1
                                                                        && x.OidName.IndexOf("memory", StringComparison.OrdinalIgnoreCase) != -1).ToList().Count > 0)
                    {
                        var ramList = dbData.StorageList.Where(x => x.OidName != null && x.OidName.IndexOf("Physical", StringComparison.OrdinalIgnoreCase) != -1
                                                                                        && x.OidName.IndexOf("memory", StringComparison.OrdinalIgnoreCase) != -1).ToList();
                        ramList = JsonConvert.DeserializeObject<List<OidReturnModule>>(JsonConvert.SerializeObject(ramList));
                        foreach (var ram in ramList)
                        {
                            ram.OidType = 20;
                        }
                        dbData.ramList = ramList;
                    }
                    else
                    {
                        var ramList = dbData.StorageList.Where(x => x.OidName != null && (x.OidName.IndexOf("memory", StringComparison.OrdinalIgnoreCase) != -1
                                                                                        || x.OidName.IndexOf("Swap space", StringComparison.OrdinalIgnoreCase) != -1)).ToList();
                        ramList = JsonConvert.DeserializeObject<List<OidReturnModule>>(JsonConvert.SerializeObject(ramList));
                        foreach (var ram in ramList)
                        {
                            ram.OidType = 20;
                        }
                        dbData.ramList = ramList;
                    }

                    // 符合系統顯示的記憶體使用率(實體記憶體-快取記憶體)
                    if (cacheMemoryList.Count > 0)
                    {
                        foreach (var ram in dbData.ramList)
                        {
                            if (ram.Used.HasValue)
                            {
                                if (cacheMemoryList[0].Used.HasValue)
                                {
                                    ram.Used = ram.Used - cacheMemoryList[0].Used;
                                }
                            }
                        }
                    }

                    // 從 ARP 表判斷網段 ip 使用狀態
                    if (dbData.ArpList != null
                    && dbData.ArpList.Count > 0)
                    {
                        // 因 ARP 表不包含自己 IP，因此加入自己 IP
                        var changeIpList = dbData.InterfaceList.Where(x => x.InterfaceStatus == 1 && !string.IsNullOrEmpty(x.InterfaceIp)).ToList();
                        foreach (var changeIp in changeIpList)
                        {
                            dbData.ArpList.Add(changeIp.InterfaceIp);
                        }
                        dbData.ArpList = dbData.ArpList.Distinct().Order().ToList();

                        if (request.ScanCidr == "0.0.0.0")
                        {
                            // dbData.ArpList
                            var cidrList = tool.GetDistinctSubnets(dbData.ArpList);
                            List<string> cidrIpList = new List<string>();
                            foreach (var cidr in cidrList)
                            {
                                List<string> cidrIpList2 = tool.ParseCidr(cidr).ToList();
                                cidrIpList2 = cidrIpList2.Distinct().ToList();

                                if (cidrIpList2.Count > 0)
                                {
                                    cidrIpList2.RemoveAt(0);
                                    cidrIpList2.RemoveAt(cidrIpList2.Count - 1);
                                }

                                cidrIpList.AddRange(cidrIpList2);
                            }
                            List<IpStatus> networkSegmentIpList3 = new List<IpStatus>();
                            foreach (var ip in cidrIpList)
                            {
                                IpStatus ipStatus = new IpStatus();
                                ipStatus.IP = ip;

                                if (dbData.ArpList.IndexOf(ip) != -1)
                                {
                                    ipStatus.Status = true;
                                }
                                else
                                {
                                    ipStatus.Status = false;
                                }

                                networkSegmentIpList3.Add(ipStatus);
                            }

                            dbData.networkSegmentIpStatus = networkSegmentIpList3;
                        }
                        else
                        {
                            dbData.networkSegmentIpStatus = JsonConvert.DeserializeObject<List<IpStatus>>(JsonConvert.SerializeObject(networkSegmentIpList2));
                            foreach (var ipData in dbData.networkSegmentIpStatus)
                            {
                                if (dbData.ArpList.IndexOf(ipData.IP) != -1
                                && dbData.networkSegmentIpStatus.Count(x => x.IP == ipData.IP) > 0)
                                {
                                    IpStatus ipStatus = dbData.networkSegmentIpStatus.Find(x => x.IP == ipData.IP);
                                    ipStatus.Status = true;
                                }
                            }
                        }

                    }
                }

                // 儲存到 Queue
                // foreach (var dbData in dbDataList)
                // {
                //     Tool.ApiTool.snmpResQueue.Enqueue(dbData);
                // }

                AddSnmpLog(dbDataList).ConfigureAwait(false);

                var resDataList = JsonConvert.DeserializeObject<List<frontSnmpTaskInfo>>(JsonConvert.SerializeObject(dbDataList));

                apiResult.ResultSet = resDataList;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                apiResult.RetCode = -1;
            }
            finally
            {
                AfterProcess();
            }

            return apiResult;
        }



        public class ScanDeviceRequest
        {
            public string? StartIp { get; set; }
            public string? EndIp { get; set; }
            public string? Community { get; set; }
            public int? Port { get; set; }
            /// <summary>
            /// 80: true
            /// </summary>
            /// <value></value>
            public int? GetTraffic { get; set; }
            /// <summary>
            /// 偵測網段 ip 哪些已使用、未使用
            /// </summary>
            /// <value></value>
            public string? ScanCidr { get; set; }
        }
        public class IpStatus
        {
            public string? IP { get; set; }
            public bool? Status { get; set; }
        }

        public class ScanIpRequest
        {
            public string? GatewayIp { get; set; }
            public string? ScanCidr { get; set; }
            public string? Community { get; set; }
            public int? Port { get; set; }
        }

        public class SnmpTaskInfoMission
        {
            public string? MissionId { get; set; }
            public string? DeviceIp { get; set; }
            public int? SnmpPort { get; set; }
            public string? Community { get; set; }
            /// <summary>
            /// 80: true
            /// </summary>
            /// <value></value>
            public int? GetTraffic { get; set; }
            public List<DeviceServiceModule> ServiceList { get; set; } = new List<DeviceServiceModule>();
            public string? ScanCidr { get; set; }
        }

        public class DeviceServiceModule
        {
            public int? Version { get; set; }
            public int? ServiceMode { get; set; }
            public int? DataType { get; set; }
            public int? OidDetail { get; set; }
            public string? ServiceName { get; set; }
            public string? Oid { get; set; }
            public string? DataKindValue { get; set; }
        }

        public class frontSnmpTaskInfo
        {
            /// <summary>
            /// 110: windows, 120: linux
            /// </summary>
            /// <value></value>
            public int? DeviceType { get; set; }
            public string? DeviceIp { get; set; }
            public string? HostName { get; set; }
            public string? SysDescr { get; set; }

            //AutoScan
            public int? CpuRate { get; set; }
            public List<OidReturnModule> InterfaceList { get; set; }
            public List<OidReturnModule> diskList { get; set; }
            public List<OidReturnModule> ramList { get; set; }

            public List<IpStatus> networkSegmentIpStatus { get; set; } = new List<IpStatus>();
        }

        public class SnmpTaskInfo : frontSnmpTaskInfo
        {
            public int? SnmpPort { get; set; }
            public string? Community { get; set; }
            public int? SnmpVersion { get; set; }
            public string? SecurityName { get; set; }
            public int? AuthProtocol { get; set; }
            public string? AuthPasswd { get; set; }
            public int? PrivacyProtocol { get; set; }
            public string? PrivacyPasswd { get; set; }
            public Dictionary<string, LastTimeData> LastDataSet { get; set; } = new Dictionary<string, LastTimeData>();
            public List<DeviceServiceModule> ServiceList { get; set; } = new List<DeviceServiceModule>();
            public List<string> ArpList { get; set; } = new List<string>();
            public string? IpForwarding { get; set; }

            //Collector
            public string? MissionId { get; set; }
            //MissionType: 10.ScanSnmpDevice, 20.Ping, 30.GetTraffic
            public int? MissionType { get; set; } = 10;
            public bool MissionIsSuccess { get; set; }

            //AutoScan
            public bool GetHddRamSize { get; set; }
            public List<OidReturnModule> StorageList { get; set; }
            public List<SnmpLog> snmpLogList { get; set; } = new List<SnmpLog>();
            // public List<SmpServiceOid> CheckDeviceOidList { get; set; }
            // public string Pid { get; set; }
        }

        public class neo4jSnmpTaskInfo
        {
            public string DeviceIp { get; set; }
            public int? SnmpPort { get; set; }
            public string Community { get; set; }
            public string HostName { get; set; }
            public string SysDescr { get; set; }
            public DateTime? CrtDate { get; set; }
            public List<neo4jOidReturnModule> InterfaceList { get; set; }
            public List<neo4jOidReturnModule> StorageList { get; set; }
        }

        public class neo4jOidReturnModule
        {
            public DateTimeOffset? Tdate { get; set; }
            public int? CpuRate { get; set; }
            public int? InterfaceStatus { get; set; }
            public int? InterfaceType { get; set; }
            public string? InterfaceIp { get; set; }
            public string? InterfaceMac { get; set; }
            public string? OidName { get; set; }
            public string? Oid { get; set; }
            public double? Total { get; set; }
            public double? Used { get; set; }
            /// <summary>
            /// 每秒平均進入 Bit
            /// </summary>
            /// <value></value>
            public double? IfInbps { get; set; }
            /// <summary>
            /// 每秒平均出去 Bit
            /// </summary>
            /// <value></value>
            public double? IfOutbps { get; set; }
        }

        public class LastTimeData
        {
            public DateTime? Time { get; set; }
            public decimal? Data { get; set; }
        }

        public class OidReturnModule
        {
            public DateTimeOffset? Tdate { get; set; }
            public int? OidType { get; set; }
            public int? InterfaceStatus { get; set; }
            public int? InterfaceType { get; set; }
            public string? InterfaceIp { get; set; }
            public string? InterfaceMac { get; set; }
            public string? OidName { get; set; }
            public string? Oid { get; set; }
            public string? OidIndex { get; set; }
            public double? Value { get; set; }
            public double? Size { get; set; }
            public double? Total { get; set; }
            public double? Used { get; set; }
            /// <summary>
            /// 每秒平均進入 Bit
            /// </summary>
            /// <value></value>
            public double? IfInbps { get; set; }
            /// <summary>
            /// 每秒平均出去 Bit
            /// </summary>
            /// <value></value>
            public double? IfOutbps { get; set; }
        }

        public class Device
        {
            public string? IP { get; set; }
            public string? Name { get; set; }
            public int? IcmpMs { get; set; }
            public List<int>? TcpPort { get; set; }
            public List<DeviceInterface>? InterfaceList { get; set; }
        }

        public class DeviceInterface
        {
            public string? Status { get; set; }
            public string? Type { get; set; }
            public string? IP { get; set; }
            public string? Index { get; set; }
            public string? Oid { get; set; }
            public string? MAC { get; set; }
            public string? Name { get; set; }
        }

        public class SnmpLog
        {
            public DateTime? Tdate { get; set; }
            public string? DeviceIp { get; set; }
            public int? SensorNo { get; set; }
            public double? Used { get; set; }
            public double? Total { get; set; }
            public int? Status { get; set; }
            public string? GetValue { get; set; }
        }

        public class ReqGetSnmpLog
        {
            public string? DeviceIp { get; set; }
        }

    }
}
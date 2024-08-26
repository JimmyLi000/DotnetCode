using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CollectorApi.Tool;
using CollectorApi.WebSocket;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Neo4jClient;
using Newtonsoft.Json;
using static CollectorApi.Controllers.Model.Common;
using static CollectorApi.WebSocket.ApiWebSocketHub;

namespace CollectorApi.Controllers.Collector
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("CorsPolicy")]
    public class HttpController : BaseApiController
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
        private string? influxHttpDbName;

        public HttpController(IHubContext<ApiWebSocketHub> hubContext, IConfiguration configuration)
        {
            _hubContext = hubContext;
            _configuration = configuration;

            var influxDBSetting = _configuration.GetSection("InfluxDB");
            influxDbToken = influxDBSetting["Token"];
            influxDbUrl = influxDBSetting["Url"];
            influxDbOrg = influxDBSetting["Organization"];
            influxHttpDbName = influxDBSetting["HttpDbName"];

            var neo4jDBSetting = _configuration.GetSection("neo4jDB");
            neu4jUri = neo4jDBSetting["Url"];
            neu4jUser = neo4jDBSetting["Account"];
            neu4jPwd = neo4jDBSetting["Password"];
        }

        [AllowAnonymous]
        [HttpGet("testApi")]
        public object testApi()
        {
            AfterProcess();
            return apiResult;
        }

        [HttpPost("GetHttpLog")]
        public async Task<object> GetHttpLog(ReqGetHttpLog request)
        {
            try
            {
                using (var client = new InfluxDBClient(influxDbUrl, token: influxDbToken))
                {
                    string fluxParam = string.Empty;

                    if (!string.IsNullOrEmpty(request.FQDN))
                    {
                        fluxParam += $@" |> filter(fn: (r) => r.FQDN == ""{request.FQDN}"") ";
                    }

                    var flux = $@" from(bucket: ""{influxHttpDbName}"")
                                    |> range(start: -1d)
                                    |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"") 
                                    |> filter(fn: (r) => r._measurement == ""httpLog"")
                                    {fluxParam}
                                    |> sort(columns: [""_time""], desc: true) ";

                    var fluxTables = await client.GetQueryApi().QueryAsync(flux, influxDbOrg).ConfigureAwait(false);

                    // DB 資料轉為物件
                    List<Dictionary<string, object>> dataList = new List<Dictionary<string, object>>();
                    foreach (var data in fluxTables)
                    {
                        foreach (var fluxRecord in data.Records)
                        {
                            var dictionary = new Dictionary<string, object>();
                            foreach (var data2 in fluxRecord.Values)
                            {
                                if (data2.Key == "DeviceAddressList")
                                {
                                    dictionary[data2.Key] = data2.Value != null ? data2.Value.ToString().Split(',').ToList() : null;
                                }
                                else if (data2.Key == "_time")
                                {
                                    dictionary["StartTime"] = data2.Value;
                                }
                                else
                                {
                                    dictionary[data2.Key] = data2.Value;
                                }
                            }
                            dataList.Add(dictionary);
                        }
                    }

                    List<influxWaaResultModel> resData = JsonConvert.DeserializeObject<List<influxWaaResultModel>>(JsonConvert.SerializeObject(dataList));

                    apiResult.RetNumber = resData.Count();
                    apiResult.ResultSet = resData;
                    apiResult.RetCode = 200;
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

        [HttpGet("GetHttp")]
        public async Task<object> GetHttp()
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.Match("(http:httpLog)");

                    var httpLogList = await query.Return((http) => new
                    {
                        DeviceAddressList = http.As<neo4jWaaResultModel>().DeviceAddressList,
                        DeviceHostName = http.As<neo4jWaaResultModel>().DeviceHostName,
                        FQDN = http.As<neo4jWaaResultModel>().FQDN,
                        FQDN_IP = http.As<neo4jWaaResultModel>().FQDN_IP,
                        HttpStatus = http.As<neo4jWaaResultModel>().HttpStatus,
                        Redirect_FQDN = http.As<neo4jWaaResultModel>().Redirect_FQDN,
                        TotalTime = http.As<neo4jWaaResultModel>().TotalTime,
                        DNSResolveTime = http.As<neo4jWaaResultModel>().DNSResolveTime,
                        TCPTime = http.As<neo4jWaaResultModel>().TCPTime,
                        DownloadTime = http.As<neo4jWaaResultModel>().DownloadTime,
                        DownloadSize = http.As<neo4jWaaResultModel>().DownloadSize,
                        FileSize = http.As<neo4jWaaResultModel>().FileSize,
                        DownloadSpeed = http.As<neo4jWaaResultModel>().DownloadSpeed,
                        StartTime = http.As<neo4jWaaResultModel>().StartTime,
                        EndTime = http.As<neo4jWaaResultModel>().EndTime
                    }).ResultsAsync.ConfigureAwait(false);

                    apiResult.RetNumber = httpLogList.Count();
                    apiResult.ResultSet = httpLogList;
                }

                apiResult.RetCode = 200;
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
        [HttpPost("ReceiveHttpLog")]
        public object ReceiveHttpLog(SendDataModel request)
        {
            if (CurrClients.TryGetValue(request.CollectorToken, out ClientInfo clientInfo))
            {
                foreach (var httpLog in request.HttpLogDataList)
                {
                    if (clientInfo.Mission.TryGetValue(httpLog.MissionId, out Mission mission))
                    {
                        mission.ResponseDataList.Add(httpLog);
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
        /// 寫入 DB
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("AddHttpLog")]
        public async Task<object> AddHttpLog(List<WaaResultModel> request)
        {
            try
            {
                var httpResList = JsonConvert.DeserializeObject<List<neo4jWaaResultModel>>(JsonConvert.SerializeObject(request));

                if (httpResList != null && httpResList.Count > 0)
                {
                    List<Task> taskList = new List<Task>();

                    taskList.Add(Task.Run(async () =>
                    {
                        using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                        {
                            await client.ConnectAsync().ConfigureAwait(false);

                            await client.Cypher
                                    .Unwind(httpResList, "data")
                                    .Merge("(fqdn:fqdn {FQDN: data.FQDN})")
                                        .OnCreate()
                                        .Set("fqdn.CrtDate = datetime({timezone: '+08:00'})")
                                        .OnMatch()
                                        .Set("fqdn.UpdDate = datetime({timezone: '+08:00'})")
                                    .Merge("(host:httpCrawlerHost {DeviceHostName: data.DeviceHostName, DeviceAddressList: data.DeviceAddressList})")
                                        .OnCreate()
                                        .Set("host.CrtDate = datetime({timezone: '+08:00'})")
                                        .OnMatch()
                                        .Set("host.UpdDate = datetime({timezone: '+08:00'})")
                                    .Merge("(http:httpLog {FQDN: data.FQDN, DeviceHostName: data.DeviceHostName, DeviceAddressList: data.DeviceAddressList})")
                                        .OnCreate()
                                        .Set("http = data, http.CrtDate = datetime({timezone: '+08:00'})")
                                        .OnMatch()
                                        .Set("http = data, http.UpdDate = datetime({timezone: '+08:00'})")
                                    .Merge("(fqdn)-[:HAS_HTTPLOG]->(http)")
                                    .Merge("(host)-[:HAS_HTTPLOG]->(http)")
                                    .ExecuteWithoutResultsAsync().ConfigureAwait(false);
                        }
                    }));

                    taskList.Add(Task.Run(async () =>
                    {
                        using (var client = new InfluxDBClient(influxDbUrl, token: influxDbToken))
                        {
                            using (var writeApi = client.GetWriteApi())
                            {
                                List<PointData> dataList = new List<PointData>();

                                foreach (var httpLog in httpResList)
                                {
                                    string? DeviceAddressList = null;
                                    if (httpLog.DeviceAddressList != null && httpLog.DeviceAddressList.Count > 0)
                                    {
                                        DeviceAddressList = string.Join(",", httpLog.DeviceAddressList);
                                    }
                                    var point = PointData
                                        .Measurement("httpLog")
                                        .Tag("FQDN", httpLog.FQDN)
                                        .Tag("DeviceHostName", httpLog.DeviceHostName)
                                        .Tag("DeviceAddressList", DeviceAddressList)
                                        .Field("FQDN_IP", httpLog.FQDN_IP)
                                        .Field("Redirect_FQDN", httpLog.Redirect_FQDN)
                                        .Field("HttpStatus", httpLog.HttpStatus)
                                        .Field("DNSResolveTime", httpLog.DNSResolveTime)
                                        .Field("TCPTime", httpLog.TCPTime)
                                        .Field("DownloadTime", httpLog.DownloadTime)
                                        .Field("DownloadSpeed", httpLog.DownloadSpeed)
                                        .Field("DownloadSize", httpLog.DownloadSize)
                                        .Field("FileSize", httpLog.FileSize)
                                        .Field("TotalTime", httpLog.TotalTime)
                                        .Timestamp(httpLog.StartTime.Value, WritePrecision.Ms);

                                    dataList.Add(point);
                                }

                                writeApi.WritePoints(points: dataList, bucket: influxHttpDbName, org: influxDbOrg);
                            }
                        }
                    }));

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

            AfterProcess();
            return apiResult;
        }

        [HttpPost("ScanHttp")]
        public async Task<object> ScanHttp(ScanHttpRequest request)
        {
            try
            {
                string fqdn = request.FQDN;
                if (tool.CreateUri(fqdn, out Uri uri, out string sUri))
                {
                    fqdn = sUri;
                }
                else
                {
                    apiResult.RetCode = -1;
                    apiResult.RetMsg = "FQDN error";
                    return apiResult;
                }

                request.FQDN = fqdn;

                // Collector 任務是否完成
                bool isMissionFinish = false;

                string missionId = Guid.NewGuid().ToString();
                var scanHttpParam = JsonConvert.DeserializeObject<ScanHttpParam>(JsonConvert.SerializeObject(request));
                scanHttpParam.ParamDefault();
                scanHttpParam.MissionId = missionId;

                // 新增任務
                Mission newMission = new Mission
                {
                    Status = 80,
                    CrtDate = DateTime.Now,
                };

                // 透過 Socket 連線下傳任務
                if (CurrClients.TryGetValue("HttpCollector", out ClientInfo clientinfo))
                {
                    clientinfo.Mission.TryAdd(scanHttpParam.MissionId, newMission);

                    string jScanHttpParam = JsonConvert.SerializeObject(scanHttpParam);

                    _hubContext.Clients.All.SendAsync("ScanHttp", jScanHttpParam);

                    // 2 分鐘內等待 Collector 回應
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    while (sw.ElapsedMilliseconds < 120 * 1000)
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

                // 完成後清除任務資料
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

                var dbData = JsonConvert.DeserializeObject<List<WaaResultModel>>(JsonConvert.SerializeObject(newMission.ResponseDataList));
                await AddHttpLog(dbData).ConfigureAwait(false);

                apiResult.ResultSet = dbData;
            }
            catch (Exception ex)
            {
                apiResult.RetMsg = ex.ToString();
                apiResult.RetCode = -1;
                logger.Error(ex);
            }
            finally
            {
                AfterProcess();
            }

            return apiResult;
        }

        public class ScanHttpRequest
        {
            public string? FQDN { get; set; }
            /// <summary>
            /// 10: GET, 20: POST
            /// </summary>
            /// <value></value>
            public int? HttpMethod { get; set; }
            /// <summary>
            /// POST Param ex: {"Param name": "value"}
            /// </summary>
            /// <value></value>
            public string? HttpContent { get; set; }
            /// <summary>
            /// Add header ex: {"Header name": "value"}
            /// </summary>
            /// <value></value>
            public string? HttpHeader { get; set; }
        }

        public class ScanHttpParam : ScanHttpRequest
        {
            public string? MissionId { get; set; }
            public int? PingRetry { get; set; }
            public int? PingTimeoutMs { get; set; }
            public int? PingIntervalMs { get; set; }
            public int? TracertMaxTtl { get; set; }
            public int? TraceRouteTimeoutMs { get; set; }
            public int? TraceRouteTimeoutLength { get; set; }
            public int? ICMPBufferSize { get; set; }
            public long? MaxDownloadSize { get; set; }
            public double? HttpTimeoutMs { get; set; }
            public int? TraceRouteMode { get; set; }
            public int? TraceRouteModeInterval { get; set; }
            // public int? RunType { get; set; }

            //預設值
            public ScanHttpParam()
            {
                PingRetry = 3;
                PingTimeoutMs = 300;
                PingIntervalMs = 1000;
                TracertMaxTtl = 31;
                TraceRouteTimeoutMs = 300;
                TraceRouteTimeoutLength = 10;
                ICMPBufferSize = 32;
                MaxDownloadSize = 51200;
                HttpTimeoutMs = 2000;
                TraceRouteMode = 2;
                TraceRouteModeInterval = 60;
            }

            /// <summary>
            /// null 的參數帶入預設值
            /// </summary>
            public void ParamDefault()
            {
                ScanHttpParam defaultParam = new ScanHttpParam();

                PingTimeoutMs = PingTimeoutMs.HasValue ? PingTimeoutMs : defaultParam.PingTimeoutMs;
                PingIntervalMs = PingIntervalMs.HasValue ? PingIntervalMs : defaultParam.PingIntervalMs;
                TracertMaxTtl = TracertMaxTtl.HasValue ? TracertMaxTtl : defaultParam.TracertMaxTtl;
                TraceRouteTimeoutMs = TraceRouteTimeoutMs.HasValue ? TraceRouteTimeoutMs : defaultParam.TraceRouteTimeoutMs;
                TraceRouteTimeoutLength = TraceRouteTimeoutLength.HasValue ? TraceRouteTimeoutLength : defaultParam.TraceRouteTimeoutLength;
                ICMPBufferSize = ICMPBufferSize.HasValue ? ICMPBufferSize : defaultParam.ICMPBufferSize;
                MaxDownloadSize = MaxDownloadSize.HasValue ? MaxDownloadSize : defaultParam.MaxDownloadSize;
                HttpTimeoutMs = HttpTimeoutMs.HasValue ? HttpTimeoutMs : defaultParam.HttpTimeoutMs;
                TraceRouteMode = TraceRouteMode.HasValue ? TraceRouteMode : defaultParam.TraceRouteMode;
                TraceRouteModeInterval = TraceRouteModeInterval.HasValue ? TraceRouteModeInterval : defaultParam.TraceRouteModeInterval;
            }
        }

        public class influxWaaResultModel
        {
            public List<string> DeviceAddressList { get; set; }
            public string DeviceHostName { get; set; }
            public string FQDN { get; set; }
            public string FQDN_IP { get; set; }
            public int? HttpStatus { get; set; }
            public string Redirect_FQDN { get; set; }
            public int? TotalTime { get; set; }     // ms
            public int? DNSResolveTime { get; set; }   // ms
            public int? TCPTime { get; set; }  // ms
            public int? DownloadTime { get; set; }  // ms
            public long? DownloadSize { get; set; } //  byte 實際下載的檔案大小
            public long? FileSize { get; set; }  // byte
            public long? DownloadSpeed { get; set; }  // Mbps
            public DateTimeOffset? StartTime { get; set; }  // 起始時間
        }
        public class neo4jWaaResultModel : influxWaaResultModel
        {
            public DateTimeOffset? EndTime { get; set; }  // 結束時間
        }

        public class WaaResultModel : neo4jWaaResultModel
        {
            public string? MissionId { get; set; }
        }

        public class ReqGetHttpLog
        {
            public string? FQDN { get; set; }
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Collector.Common;
using SnmpSharpNet;

namespace Collector.Snmp
{
    public class SnmpMission
    {
        private static object locker = new Object();

        public async Task<List<SnmpTaskInfo>> RunSnmpMission(List<SnmpTaskInfoMission> missionList)
        {
            // 無設備 ip 任務展開為內網任務
            for (int num = 0; num < missionList.Count; num++)
            {
                int deviceNum = num;
                SnmpTaskInfoMission device = missionList[deviceNum];

                if (string.IsNullOrEmpty(device.DeviceIp))
                {
                    var DeviceHostName = Dns.GetHostName();
                    var DeviceAddressList = (await Tool.GetLocalAddress()).Select(ip => ip.ToString()).Where(ip => !ip.EndsWith(".0.1")).ToList();
                    foreach (var localIp in DeviceAddressList)
                    {
                        var lanIpList = Tool.GenerateSubnetIps(localIp);
                        foreach (var lanIp in lanIpList)
                        {
                            SnmpTaskInfoMission missionData = new SnmpTaskInfoMission
                            {
                                MissionId = device.MissionId,
                                DeviceIp = lanIp,
                                SnmpPort = device.SnmpPort,
                                SnmpVersion = device.SnmpVersion,
                                Community = device.Community,
                                ServiceList = device.ServiceList,
                                GetTraffic = device.GetTraffic,
                                ScanCidr = device.ScanCidr
                            };
                            missionList.Add(missionData);
                        }
                    }
                }
            }

            // 過濾掉無 ip 任務
            missionList = missionList.Where(x => !string.IsNullOrEmpty(x.DeviceIp)).ToList();
            Console.WriteLine($"ScanIP: {string.Join(',', missionList.Select(x => x.DeviceIp))}");

            List<SnmpTaskInfo> snmpRawDataList = new List<SnmpTaskInfo>();
            List<Task> taskList = new List<Task>();

            for (int num = 0; num < missionList.Count; num++)
            {
                int deviceNum = num;
                SnmpTaskInfoMission device = missionList[deviceNum];

                taskList.Add(Task.Run(async () =>
                {
                    try
                    {
                        DeviceData deviceData = new DeviceData();
                        deviceData.DeviceIp = device.DeviceIp;
                        deviceData.SnmpDataObj = new ConcurrentDictionary<string, List<DeviceData.RawData>>();

                        SnmpTaskInfo snmpDevice = new SnmpTaskInfo();
                        snmpDevice.MissionId = device.MissionId;
                        snmpDevice.DeviceIp = device.DeviceIp;
                        snmpDevice.SnmpPort = device.SnmpPort;
                        snmpDevice.SnmpVersion = device.SnmpVersion;
                        snmpDevice.Community = device.Community;
                        snmpDevice.ServiceList = device.ServiceList;
                        snmpDevice.MissionIsSuccess = false;

                        deviceData.SnmpDataObj = await ScanSensor(snmpDevice, device.ServiceList, isRealTimeTrafficEnd: 90, fullDetect: false);

                        // SysDescr
                        var firstData = device.ServiceList.Where(x => x.ServiceMode == 122).ToList();
                        if (firstData.Count > 0)
                        {
                            string dataKey = firstData[0].Oid;
                            if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                            {
                                if (snmpResult != null && snmpResult.Count > 0)
                                {
                                    if (snmpResult[0].Data != null && snmpResult[0].Data.Count > 0)
                                    {
                                        snmpDevice.SysDescr = snmpResult[0].Data[0];
                                        firstData[0].DataKindValue = snmpResult[0].Data[0];
                                    }
                                }
                            }
                        }

                        // ipForwarding
                        firstData = device.ServiceList.Where(x => x.ServiceMode == 123).ToList();
                        if (firstData.Count > 0)
                        {
                            string dataKey = firstData[0].Oid;
                            if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                            {
                                if (snmpResult != null && snmpResult.Count > 0)
                                {
                                    if (snmpResult[0].Data != null && snmpResult[0].Data.Count > 0)
                                    {
                                        snmpDevice.IpForwarding = snmpResult[0].Data[0];
                                        firstData[0].DataKindValue = snmpResult[0].Data[0];
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(snmpDevice.SysDescr) && snmpDevice.SysDescr.ToLower().Contains("windows"))
                        {
                            snmpDevice.DeviceType = 110;
                        }
                        else
                        {
                            snmpDevice.DeviceType = 120;
                        }
                        if (snmpDevice.IpForwarding == "1")
                        {
                            snmpDevice.DeviceType = 130;
                        }

                        // HostName
                        firstData = device.ServiceList.Where(x => x.ServiceMode == 121).ToList();
                        if (firstData.Count > 0)
                        {
                            string dataKey = firstData[0].Oid;
                            if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                            {
                                if (snmpResult != null && snmpResult.Count > 0)
                                {
                                    if (snmpResult[0].Data != null && snmpResult[0].Data.Count > 0)
                                    {
                                        snmpDevice.HostName = snmpResult[0].Data[0];
                                        firstData[0].DataKindValue = snmpResult[0].Data[0];
                                    }
                                }
                            }
                        }

                        // CPU
                        firstData = device.ServiceList.Where(x => x.ServiceMode == 10).ToList();
                        if (firstData.Count > 0)
                        {
                            string dataKey = firstData[0].Oid;
                            if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                            {
                                if (snmpResult != null && snmpResult.Count > 0)
                                {
                                    if (snmpResult[0].Data != null && snmpResult[0].Data.Count > 0)
                                    {
                                        List<int> cpuValueList = new List<int>();
                                        foreach (var cpuValue in snmpResult[0].Data)
                                        {
                                            if (int.TryParse(cpuValue, out int value))
                                            {
                                                cpuValueList.Add(value);
                                            }
                                        }
                                        snmpDevice.CpuRate = (int)cpuValueList.Average();
                                        firstData[0].DataKindValue = string.Join(',', snmpResult[0].Data);

                                        {
                                            SnmpLog snmpLog = new SnmpLog();
                                            snmpLog.Tdate = snmpResult[0].Tdate;
                                            snmpLog.SensorNo = snmpResult[0].DataKind;
                                            snmpLog.DeviceIp = deviceData.DeviceIp;
                                            snmpLog.Used = cpuValueList.Average();
                                            snmpLog.Total = snmpResult[0].Data.Count;
                                            snmpLog.GetValue = string.Join(',', snmpResult[0].Data);
                                            snmpLog.Status = snmpLog.Used.HasValue ? 80 : 90;
                                            snmpDevice.snmpLogList.Add(snmpLog);
                                        }

                                    }
                                }
                            }
                        }

                        // Disk
                        firstData = device.ServiceList.Where(x => x.ServiceMode == 85).ToList();
                        if (firstData.Count > 0)
                        {
                            string? dataKey = firstData[0].Oid;
                            if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                            {
                                if (snmpResult != null && snmpResult.Count > 0)
                                {
                                    snmpDevice.StorageList.AddRange(snmpResult[0].StorageInterfaceList);

                                    foreach (OidReturnModule singleStorage in snmpDevice.StorageList)
                                    {
                                        // 偵測 Storage Size
                                        if (int.TryParse(singleStorage.OidIndex, out int OidIndex))
                                        {
                                            // BlockSize
                                            var _oidinfo = new DeviceServiceModule();
                                            _oidinfo.Oid = $".1.3.6.1.2.1.25.2.3.1.4.{OidIndex}";
                                            _oidinfo.DataType = 10;
                                            _oidinfo.OidDetail = 10;
                                            _oidinfo.ServiceName = $"BlockSize";
                                            List<string?> snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                            if (snmpResList.Count > 0)
                                            {
                                                if (int.TryParse(snmpResList[0], out int value))
                                                {
                                                    singleStorage.Size = value;
                                                }
                                            }

                                            // Total
                                            _oidinfo = new DeviceServiceModule();
                                            _oidinfo.Oid = $".1.3.6.1.2.1.25.2.3.1.5.{OidIndex}";
                                            _oidinfo.DataType = 10;
                                            _oidinfo.OidDetail = 20;
                                            _oidinfo.ServiceName = $"Total";
                                            snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                            if (snmpResList.Count > 0)
                                            {
                                                if (int.TryParse(snmpResList[0], out int value))
                                                {
                                                    // 單位轉為 MB
                                                    singleStorage.Total = value;
                                                    singleStorage.Total = singleStorage.Total * singleStorage.Size;
                                                    singleStorage.Total = singleStorage.Total / 1024 / 1024;
                                                }
                                            }

                                            // Used
                                            _oidinfo = new DeviceServiceModule();
                                            _oidinfo.Oid = $".1.3.6.1.2.1.25.2.3.1.6.{OidIndex}";
                                            _oidinfo.DataType = 10;
                                            _oidinfo.OidDetail = 30;
                                            _oidinfo.ServiceName = $"Used";
                                            snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                            if (snmpResList.Count > 0)
                                            {
                                                if (int.TryParse(snmpResList[0], out int value))
                                                {
                                                    // 單位轉為 MB
                                                    singleStorage.Used = value;
                                                    singleStorage.Used = singleStorage.Used * singleStorage.Size;
                                                    singleStorage.Used = singleStorage.Used / 1024 / 1024;
                                                }
                                            }
                                        }

                                    }
                                }
                            }
                        }

                        // Interface
                        firstData = device.ServiceList.Where(x => x.ServiceMode == 80).ToList();
                        if (firstData.Count > 0)
                        {
                            string? dataKey = firstData[0].Oid;
                            if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                            {
                                if (snmpResult != null && snmpResult.Count > 0)
                                {
                                    if (snmpResult[0].InterfaceList != null && snmpResult[0].InterfaceList.Count > 0)
                                    {
                                        snmpDevice.InterfaceList.AddRange(snmpResult[0].InterfaceList);

                                        // Get interface IP
                                        var _oidinfo = new DeviceServiceModule();
                                        _oidinfo.ServiceMode = 60;
                                        _oidinfo.Oid = $".1.3.6.1.2.1.4.20.1.2";
                                        _oidinfo.DataType = 20;
                                        _oidinfo.OidDetail = 80;
                                        _oidinfo.ServiceName = $"IfIpIndex";
                                        var IfIpIndexList = await ScanInterfaceDetail(snmpDevice, _oidinfo);

                                        _oidinfo = new DeviceServiceModule();
                                        _oidinfo.ServiceMode = 61;
                                        _oidinfo.Oid = $".1.3.6.1.2.1.4.20.1.1";
                                        _oidinfo.DataType = 20;
                                        _oidinfo.OidDetail = 80;
                                        _oidinfo.ServiceName = $"IfIp";
                                        var IfIpList = await ScanInterfaceDetail(snmpDevice, _oidinfo);

                                        foreach (OidReturnModule singleInterface in snmpDevice.InterfaceList)
                                        {
                                            // NetInterface
                                            singleInterface.OidType = 10;

                                            // Get interface IP
                                            var ipIndex = IfIpIndexList.IndexOf(singleInterface.OidIndex);
                                            if (ipIndex != -1)
                                            {
                                                singleInterface.InterfaceIp = $"{IfIpList[ipIndex]}";
                                            }

                                            // 偵測網卡 Status, Type, Mac..
                                            if (int.TryParse(singleInterface.OidIndex, out int oidIndex))
                                            {
                                                // IfStatus
                                                _oidinfo = new DeviceServiceModule();
                                                _oidinfo.ServiceMode = 5;
                                                _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.8.{oidIndex}";
                                                _oidinfo.DataType = 10;
                                                _oidinfo.OidDetail = 80;
                                                _oidinfo.ServiceName = $"IfLink";
                                                List<string?> snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                                if (snmpResList.Count > 0)
                                                {
                                                    if (int.TryParse(snmpResList[0], out int value))
                                                    {
                                                        singleInterface.InterfaceStatus = value;
                                                    }
                                                }

                                                // IfType
                                                _oidinfo = new DeviceServiceModule();
                                                _oidinfo.ServiceMode = 55;
                                                _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.3.{oidIndex}";
                                                _oidinfo.DataType = 10;
                                                _oidinfo.OidDetail = 80;
                                                _oidinfo.ServiceName = $"IfType";
                                                snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                                if (snmpResList.Count > 0)
                                                {
                                                    if (int.TryParse(snmpResList[0], out int value))
                                                    {
                                                        singleInterface.InterfaceType = value;
                                                    }
                                                }

                                                // IfMac
                                                _oidinfo = new DeviceServiceModule();
                                                _oidinfo.ServiceMode = 50;
                                                _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.6.{oidIndex}";
                                                _oidinfo.DataType = 10;
                                                _oidinfo.OidDetail = 80;
                                                _oidinfo.ServiceName = $"IfMac";
                                                snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                                if (snmpResList.Count > 0)
                                                {
                                                    singleInterface.InterfaceMac = snmpResList[0];
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // ARP IP
                        firstData = device.ServiceList.Where(x => x.ServiceMode == 65).ToList();
                        if (firstData.Count > 0)
                        {
                            if (!string.IsNullOrEmpty(device.ScanCidr))
                            {
                                var scanCidrList = device.ScanCidr.Split('.').ToList();

                                if (scanCidrList.Count >= 2)
                                {
                                    string scanCidrCidr = $"{scanCidrList[0]}.{scanCidrList[1]}.{scanCidrList[2]}.";

                                    string dataKey = firstData[0].Oid;
                                    if (deviceData.SnmpDataObj.TryGetValue(dataKey, out List<DeviceData.RawData> snmpResult))
                                    {
                                        if (snmpResult != null && snmpResult.Count > 0)
                                        {
                                            if (snmpResult[0].Data != null && snmpResult[0].Data.Count > 0)
                                            {
                                                snmpDevice.ArpList = snmpResult[0].Data;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // 每秒平均流量
                        if (device.GetTraffic == 80)
                        {
                            // 取得第一次流量
                            foreach (var netInterface in snmpDevice.InterfaceList)
                            {
                                if (int.TryParse(netInterface.OidIndex, out int oidIndex) &&
                                    netInterface.InterfaceStatus == 1)
                                {
                                    // In
                                    var _oidinfo = new DeviceServiceModule();
                                    _oidinfo.ServiceMode = 40;
                                    _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.10.{oidIndex}";
                                    _oidinfo.DataType = 10;
                                    _oidinfo.OidDetail = 30;
                                    _oidinfo.ServiceName = $"IfInByte";
                                    var snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);

                                    // Out
                                    _oidinfo = new DeviceServiceModule();
                                    _oidinfo.ServiceMode = 45;
                                    _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.16.{oidIndex}";
                                    _oidinfo.DataType = 10;
                                    _oidinfo.OidDetail = 30;
                                    _oidinfo.ServiceName = $"IfOutBype";
                                    snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                }
                            }

                            // 流量變動
                            bool changeTraffic = false;

                            // 取得第二次流量 (ScanInterfaceDetail 有回資料代表已算出差值與平均每秒流量)
                            Stopwatch sw = new Stopwatch();
                            sw.Start();
                            while (sw.ElapsedMilliseconds < 60 * 1000 && snmpDevice.InterfaceList != null && snmpDevice.InterfaceList.Count > 0 && !changeTraffic)
                            {
                                await Task.Delay(1000);

                                foreach (var netInterface in snmpDevice.InterfaceList)
                                {
                                    if (int.TryParse(netInterface.OidIndex, out int oidIndex) &&
                                        netInterface.InterfaceStatus == 1)
                                    {
                                        // In
                                        var _oidinfo = new DeviceServiceModule();
                                        _oidinfo.ServiceMode = 40;
                                        _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.10.{oidIndex}";
                                        _oidinfo.DataType = 10;
                                        _oidinfo.OidDetail = 30;
                                        _oidinfo.ServiceName = $"IfInByte";
                                        var snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                        if (snmpResList.Count > 0)
                                        {
                                            changeTraffic = true;
                                        }

                                        // Out
                                        _oidinfo = new DeviceServiceModule();
                                        _oidinfo.ServiceMode = 45;
                                        _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.16.{oidIndex}";
                                        _oidinfo.DataType = 10;
                                        _oidinfo.OidDetail = 30;
                                        _oidinfo.ServiceName = $"IfOutBype";
                                        snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                        if (snmpResList.Count > 0)
                                        {
                                            changeTraffic = true;
                                        }
                                    }
                                }
                            }
                            sw.Stop();

                            // 流量變動
                            changeTraffic = false;
                            // 取得第三次流量 (ScanInterfaceDetail 有回資料代表已算出差值與平均每秒流量)
                            sw.Restart();
                            while (sw.ElapsedMilliseconds < 60 * 1000 && snmpDevice.InterfaceList != null && snmpDevice.InterfaceList.Count > 0 && !changeTraffic)
                            {
                                await Task.Delay(1000);

                                foreach (var netInterface in snmpDevice.InterfaceList)
                                {
                                    if (int.TryParse(netInterface.OidIndex, out int oidIndex) &&
                                        netInterface.InterfaceStatus == 1)
                                    {
                                        // In
                                        var _oidinfo = new DeviceServiceModule();
                                        _oidinfo.ServiceMode = 40;
                                        _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.10.{oidIndex}";
                                        _oidinfo.DataType = 10;
                                        _oidinfo.OidDetail = 30;
                                        _oidinfo.ServiceName = $"IfInByte";
                                        var snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                        if (snmpResList.Count > 0)
                                        {
                                            if (double.TryParse(snmpResList[0], out double value))
                                            {
                                                netInterface.IfInbps = value;
                                                changeTraffic = true;
                                            }
                                        }

                                        // Out
                                        _oidinfo = new DeviceServiceModule();
                                        _oidinfo.ServiceMode = 45;
                                        _oidinfo.Oid = $".1.3.6.1.2.1.2.2.1.16.{oidIndex}";
                                        _oidinfo.DataType = 10;
                                        _oidinfo.OidDetail = 30;
                                        _oidinfo.ServiceName = $"IfOutBype";
                                        snmpResList = await ScanInterfaceDetail(snmpDevice, _oidinfo);
                                        if (snmpResList.Count > 0)
                                        {
                                            if (double.TryParse(snmpResList[0], out double value))
                                            {
                                                netInterface.IfOutbps = value;
                                                changeTraffic = true;
                                            }
                                        }
                                    }
                                }
                            }
                            sw.Stop();
                        }

                        if (snmpDevice.MissionIsSuccess)
                        {
                            lock (locker)
                            {
                                snmpRawDataList.Add(snmpDevice);
                            }
                        }

                    }
                    catch
                    {

                    }

                }));
            }

            // 等待完成 (若使用同步，無法同時處理多個請求)
            Task t = Task.WhenAll(taskList);
            await t.ConfigureAwait(false);

            return snmpRawDataList;
        }

        public async Task<ConcurrentDictionary<string, List<DeviceData.RawData>>> ScanSensor(SnmpTaskInfo snmpDevice, List<DeviceServiceModule> oidList
                        , int? isRealTimeTrafficEnd = null, bool fullDetect = false)
        {
            var oidDict = new ConcurrentDictionary<string, List<DeviceData.RawData>>();

            SnmpParam snmpParam = new SnmpParam();
            snmpParam.IP = snmpDevice.DeviceIp;
            snmpParam.Port = snmpDevice.SnmpPort.HasValue ? snmpDevice.SnmpPort.Value : 161;
            snmpParam.Version = snmpDevice.SnmpVersion.HasValue ? (eSnmpVersion)snmpDevice.SnmpVersion.Value : eSnmpVersion.v2c;
            snmpParam.Community = snmpDevice.Community;

            bool isTimeout = false;

            SnmpFunction snmpFunc = new SnmpFunction();

            foreach (var oidInfo in oidList)
            {
                var oidDetailDict = new DeviceData.RawData
                {
                    OidDetail = oidInfo.OidDetail,
                    Data = new List<string?>(),//[0] Used, [1] Total 
                    Tdate = DateTime.Now
                };

                LastTimeData lastData = new LastTimeData();

                try
                {
                    if (oidInfo.DataType == 10)
                    {
                        List<string?> first = new List<string?>();

                        try
                        {
                            //若出現 Timeout
                            if (isTimeout && !fullDetect)
                            {
                                throw new SnmpException("SnmpTimeout");
                            }
                            first = await snmpFunc.OidGet(snmpParam, oidInfo);
                        }
                        catch
                        {
                            // timeout 略過後續 snmp 偵測
                            isTimeout = true;
                            first.Add(null);
                        }

                        lastData.Time = DateTime.Now;

                        if (first.Count == 0)
                        {
                            continue;
                        }

                        if (!decimal.TryParse(first[0], out decimal value))
                        {
                            if (oidInfo.IsContinuousData())
                            {
                                if (snmpDevice.LastDataSet.ContainsKey(oidInfo.Oid))
                                {
                                    //[0] Used
                                    oidDetailDict.Data.Add(null);
                                    //[1] Total 累積量
                                    oidDetailDict.Data.Add(null);
                                }
                                else
                                {
                                    snmpDevice.LastDataSet.Add(oidInfo.Oid, lastData);
                                    snmpDevice.LastDataSet[oidInfo.Oid].Data = value;
                                }
                            }
                            else
                            {
                                oidDetailDict.Data = first;
                            }
                        }
                        else
                        {
                            decimal snmpResultData = decimal.Parse(first[0]);

                            if (oidInfo.IsContinuousData())
                            {
                                if (snmpDevice.LastDataSet.ContainsKey(oidInfo.Oid))
                                {
                                    // 計算流量差值

                                    // 檢查上次值如果是 Null 就略過，因無法計算差值
                                    if (!snmpDevice.LastDataSet[oidInfo.Oid].Data.HasValue)
                                    {
                                        continue;
                                    }
                                    if (!snmpDevice.LastDataSet[oidInfo.Oid].Time.HasValue || !lastData.Time.HasValue)
                                    {
                                        continue;
                                    }

                                    // 算出每秒平均值 Used
                                    TimeSpan ts = lastData.Time.Value - snmpDevice.LastDataSet[oidInfo.Oid].Time.Value;

                                    // 偵測間隔小於 500 毫秒，即捨棄這次偵測，因為可能出現除數為零，無法計算
                                    if (ts.TotalMilliseconds < 500 && (oidInfo.ServiceMode == 40 || oidInfo.ServiceMode == 45) && oidInfo.OidDetail == 30)
                                    {
                                        continue;
                                    }

                                    if ((oidInfo.ServiceMode == 40 || oidInfo.ServiceMode == 45) && oidInfo.OidDetail == 30)
                                    {
                                        // 流量 bps
                                        var strUsed = ((double)(snmpResultData - snmpDevice.LastDataSet[oidInfo.Oid].Data) / ts.TotalSeconds * 8d).ToString();

                                        if (double.TryParse(strUsed, out double used))
                                        {
                                            // 目的是只記錄有變動的流量，避免即時流量算錯 (錯誤: Collector 每 10 秒偵測一次，ESX 每 50 秒變動一次流量，但除以秒數卻是除以 10)
                                            if (used == 0
                                                && snmpDevice.LastDataSet[oidInfo.Oid].Data != 0
                                                && isRealTimeTrafficEnd.HasValue
                                                && isRealTimeTrafficEnd != 80)
                                            {
                                                continue;
                                            }

                                            // 上次流量與現在時間差
                                            TimeSpan tsTraffic = DateTime.Now - snmpDevice.LastDataSet[oidInfo.Oid].Time.Value;

                                            // 2 分鐘內持續 0 流量就捨棄，超過 2 分鐘就紀錄流量
                                            if (used == 0
                                                && tsTraffic.TotalSeconds < 120)
                                            {
                                                continue;
                                            }
                                        }

                                        //[0] Used
                                        oidDetailDict.Data.Add(strUsed);
                                    }
                                    else
                                    {
                                        //[0] Used
                                        oidDetailDict.Data.Add((snmpResultData - snmpDevice.LastDataSet[oidInfo.Oid].Data).ToString());
                                    }

                                    //[1] Total 累積量
                                    oidDetailDict.Data.Add(snmpResultData.ToString());
                                    oidDetailDict.LastTdate = snmpDevice.LastDataSet[oidInfo.Oid].Time;
                                    snmpDevice.LastDataSet[oidInfo.Oid].Data = snmpResultData;
                                    snmpDevice.LastDataSet[oidInfo.Oid].Time = lastData.Time;
                                }
                                else
                                {
                                    snmpDevice.LastDataSet.Add(oidInfo.Oid, lastData);
                                    snmpDevice.LastDataSet[oidInfo.Oid].Data = snmpResultData;
                                }
                            }
                            else
                            {
                                oidDetailDict.Data = first;
                            }
                        }

                    }
                    else if (oidInfo.DataType == 20)
                    {
                        if (oidInfo.ServiceMode == 80)
                        {
                            try
                            {
                                //若出現 Timeout
                                if (isTimeout && !fullDetect)
                                {
                                    throw new SnmpException("SnmpTimeout");
                                }
                                oidDetailDict.InterfaceList = await snmpFunc.OidGetBulk_InterfaceOrStorage(snmpParam, oidInfo);
                            }
                            catch (SnmpException ex)
                            {
                                // timeout 略過後續 snmp 偵測
                                isTimeout = true;
                            }

                            // Get oid index
                            foreach (var InterfaceItem in oidDetailDict.InterfaceList)
                            {
                                InterfaceItem.Tdate = DateTime.Now;
                                List<string> valueList = InterfaceItem.Oid.Split('.').ToList();

                                if (valueList.Count == 0)
                                {
                                    continue;
                                }

                                string LastIndexValue = valueList[valueList.Count - 1];
                                InterfaceItem.OidIndex = LastIndexValue;
                            }
                        }
                        else if (oidInfo.ServiceMode == 85)
                        {
                            try
                            {
                                //若出現 Timeout
                                if (isTimeout && !fullDetect)
                                {
                                    throw new SnmpException("SnmpTimeout");
                                }
                                oidDetailDict.StorageInterfaceList = await snmpFunc.OidGetBulk_InterfaceOrStorage(snmpParam, oidInfo);
                            }
                            catch (SnmpException ex)
                            {
                                // timeout 略過後續 snmp 偵測
                                isTimeout = true;
                            }

                            // Get oid index
                            foreach (var storageItem in oidDetailDict.StorageInterfaceList)
                            {
                                storageItem.Tdate = DateTime.Now;
                                List<string> valueList = storageItem.Oid.Split('.').ToList();

                                if (valueList.Count == 0)
                                {
                                    continue;
                                }

                                string LastIndexValue = valueList[valueList.Count - 1];
                                storageItem.OidIndex = LastIndexValue;
                            }
                        }
                        else
                        {
                            try
                            {
                                //若出現 Timeout
                                if (isTimeout && !fullDetect)
                                {
                                    throw new SnmpException("SnmpTimeout");
                                }
                                oidDetailDict.Data = await snmpFunc.OidGetBulk(snmpParam, oidInfo);
                            }
                            catch (SnmpException ex)
                            {
                                // timeout 略過後續 snmp 偵測
                                isTimeout = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    isTimeout = true;
                    oidDetailDict.Data = null;
                }
                finally
                {
                    oidDetailDict.EndDate = DateTime.Now;
                }

                if (oidList != null)
                {
                    var smpService = oidList.Where(x => x.ServiceMode == oidInfo.ServiceMode).ToList();
                    if (smpService != null && smpService.Count > 0)
                    {
                        oidDetailDict.DataKind = oidInfo.ServiceMode;
                    }
                }

                snmpDevice.MissionIsSuccess = isTimeout ? false : true;

                if (!oidDict.ContainsKey(oidInfo.Oid))
                {
                    oidDict[oidInfo.Oid] = new List<DeviceData.RawData>();
                }

                oidDetailDict.CollectorDataKey = oidInfo.Oid;
                oidDict[oidInfo.Oid].Add(oidDetailDict);

                // 呼叫 SNMP 結束後押上 Tdate
                oidDetailDict.Tdate = DateTime.Now;
            }

            return oidDict;
        }

        /// <summary>
        /// interface status, type, mac..
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        public async Task<List<string?>> ScanInterfaceDetail(SnmpTaskInfo device, DeviceServiceModule oidInfo)
        {
            List<string?> snmpValueList = new List<string?>();

            try
            {
                var OidList = new List<DeviceServiceModule>();
                OidList.Add(oidInfo);

                // Call SNMP
                ConcurrentDictionary<string, List<DeviceData.RawData>> _snmpRawData = await ScanSensor(device, OidList, isRealTimeTrafficEnd: 90, fullDetect: false);

                if (_snmpRawData.TryGetValue(oidInfo.Oid, out List<DeviceData.RawData> _snmpRawData2))
                {
                    if (_snmpRawData2 != null && _snmpRawData2.Count > 0)
                    {
                        foreach (var snmpvalue in _snmpRawData2[0].Data)
                        {
                            snmpValueList.Add(snmpvalue);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return snmpValueList;
        }

        /// <summary>
        /// 產生Snmp任務清單
        /// </summary>
        /// <returns></returns>
        public static List<DeviceServiceModule> CreateSensorList()
        {
            List<DeviceServiceModule> resultList = new List<DeviceServiceModule>();

            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 10;
                mission.ServiceName = "CPU";
                mission.Oid = ".1.3.6.1.2.1.25.3.3.1.2";
                mission.OidDetail = 30;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 121;
                mission.ServiceName = "HostName";
                mission.Oid = ".1.3.6.1.2.1.1.5.0";
                mission.OidDetail = 80;
                mission.DataType = 10;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 122;
                mission.ServiceName = "SysDescr";
                mission.Oid = ".1.3.6.1.2.1.1.1.0";
                mission.OidDetail = 80;
                mission.DataType = 10;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 80;
                mission.ServiceName = "NetInterface";
                mission.Oid = ".1.3.6.1.2.1.2.2.1.2";
                mission.OidDetail = 80;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 85;
                mission.ServiceName = "StorageInterface";
                mission.Oid = ".1.3.6.1.2.1.25.2.3.1.3";
                mission.OidDetail = 80;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 65;
                mission.ServiceName = "RegisterIp";
                mission.Oid = ".1.3.6.1.2.1.4.22.1.3";
                mission.OidDetail = 80;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 123;
                mission.ServiceName = "ipForwarding";
                mission.Oid = ".1.3.6.1.2.1.4.1.0";
                mission.OidDetail = 80;
                mission.DataType = 10;
                resultList.Add(mission);
            }

            return resultList;
        }
    }
}
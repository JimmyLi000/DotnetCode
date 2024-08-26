using System.Collections.Concurrent;
using SnmpSharpNet;

public class SnmpTaskInfo
{
    /// <summary>
    /// 110: windows, 120: linux
    /// </summary>
    /// <value></value>
    public int? DeviceOs { get; set; }
    public string? DeviceIp { get; set; }
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
    public string? HostName { get; set; }
    public string? SysDescr { get; set; }

    //Collector
    public string? MissionId { get; set; }
    //MissionType: 10.ScanSnmpDevice, 20.Ping, 30.GetTraffic
    public int? MissionType { get; set; } = 10;
    public bool MissionIsSuccess { get; set; }

    //AutoScan
    public int? CpuRate { get; set; }
    public bool GetHddRamSize { get; set; }
    public List<OidReturnModule> InterfaceList { get; set; } = new List<OidReturnModule>();
    public List<OidReturnModule> StorageList { get; set; } = new List<OidReturnModule>();
    // public List<SmpServiceOid> CheckDeviceOidList { get; set; }
    // public string Pid { get; set; }

    /*
        /// <summary>
        /// fullDetect: true 每個 oid 都一定要偵測
        /// </summary>
        /// <param name="oidList"></param>
        /// <param name="isRealTimeTrafficEnd"></param>
        /// <param name="fullDetect"></param>
        /// <returns></returns>
        internal async Task<ConcurrentDictionary<int, List<DeviceData.RawData>>> GetSnmpData_ParamOid(List<DeviceServiceModule> oidList, int? isRealTimeTrafficEnd = null
            , bool fullDetect = false)
        {
            // Snmp參數
            SnmpParam snmpParam = new SnmpParam();
            snmpParam.Version = SnmpVersion.HasValue ? (eSnmpVersion)SnmpVersion : eSnmpVersion.v2c;
            snmpParam.DeviceName = DeviceID;
            snmpParam.DeviceNo = DeviceNo;
            snmpParam.IP = DeviceIp;
            snmpParam.Port = SnmpPort ?? 161;
            snmpParam.Community = string.IsNullOrEmpty(Community) ? "public" : Community;
            snmpParam.SecurityName = !string.IsNullOrEmpty(SecurityName) ? SecurityName : string.Empty;
            snmpParam.AuthProtocol = AuthProtocol.HasValue ? (eSnmpAuthProtocol)AuthProtocol : 0;
            snmpParam.AuthPassword = !string.IsNullOrEmpty(AuthPasswd) ? AuthPasswd : string.Empty;
            snmpParam.PrivacyProtocol = PrivacyProtocol.HasValue ? (eSnmpPrivacyProtocol)PrivacyProtocol : 0;
            snmpParam.PrivacyPassword = !string.IsNullOrEmpty(PrivacyPasswd) ? PrivacyPasswd : string.Empty;

            // 所有 oid 放在同一個物件
            var oidDict = new ConcurrentDictionary<int, List<DeviceData.RawData>>();

            // SnmpTimeout 終止後續偵測
            bool deviceSnmpIsTimeout = false;


        }
        */
}

public class LastTimeData
{
    public DateTime? Time { get; set; }
    public decimal? Data { get; set; }
}

public class SnmpTaskInfoMission
{
    public string? MissionId { get; set; }
    public string? DeviceIp { get; set; }
    public int? SnmpPort { get; set; }
    public int? SnmpVersion { get; set; }
    public string? Community { get; set; }
    /// <summary>
    /// 80: true
    /// </summary>
    /// <value></value>
    public int? GetTraffic { get; set; }
    public List<DeviceServiceModule> ServiceList { get; set; } = new List<DeviceServiceModule>();
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

public class SendDataModel
{
    public string? CollectorToken { get; set; }
    public List<WaaResultModel>? HttpLogDataList { get; set; }
}

public class WaaResultModel
{
    public string? MissionId { get; set; }
    public List<string> DeviceAddressList { get; set; }
    public string DeviceHostName { get; set; }
    public string FQDN { get; set; }
    public string FQDN_IP { get; set; }
    public int? HttpStatus { get; set; }
    public string Redirect_FQDN { get; set; } // 重新導向網址 , 若無導向則為NULL
    public int? TotalTime { get; set; }     // ms
    public int? DNSResolveTime { get; set; }   // ms
    public int? TCPTime { get; set; }  // ms
    public int? DownloadTime { get; set; }  // ms
    public long? DownloadSize { get; set; } //  byte 實際下載的檔案大小
    public long? FileSize { get; set; }  // byte
    public long? DownloadSpeed { get; set; }  // Mbps
    public DateTimeOffset? StartTime { get; set; }  // 起始時間
    public DateTimeOffset? EndTime { get; set; }  // 結束時間
}

public class DeviceServiceModule
{
    public int? ServiceMode { get; set; }
    public int? DataType { get; set; }
    public int? OidDetail { get; set; }
    public string? ServiceName { get; set; }
    public string? Oid { get; set; }
    public string? DataKindValue { get; set; }

    internal bool IsContinuousData()
    {
        switch (ServiceMode)
        {
            case 40:
            case 45:
                {
                    if (OidDetail == 30)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            case 101:
            case 102:
            case 106:
            case 107:
                return true;
            default:
                return false;
        }
    }
}

public class SnmpParam
{
    public UdpTarget? Target { get; set; }
    public string? IP { get; set; }
    public int Port { get; set; }
    public string? Community { get; set; }
    public string? SecurityName { get; set; }
    public string? SecurityNameHidden { get; set; }
    public eSnmpVersion Version { get; set; }
    public eSnmpAuthProtocol AuthProtocol { get; set; }
    public string? AuthPassword { get; set; }
    public string? AuthPasswordHidden { get; set; }
    public eSnmpPrivacyProtocol PrivacyProtocol { get; set; }
    public string? PrivacyPassword { get; set; }
    public string? PrivacyPasswordHidden { get; set; }
}

/*
public class DeviceServiceModule
{
    public int? DevServiceNo { get; set; }
    public int ServiceMode { get; set; }
    public int? Status { get; set; }
    public int? DataType { get; set; }
    public int? OidDetail { get; set; }
    public int? SensorType { get; set; }

    /// <summary>
    /// 10.Smp, 20.WAA, 30.Ping
    /// </summary>
    public int? ConnectType { get; set; }

    public string? ServiceName { get; set; }
    public string? URI { get; set; }
    public string? Oid { get; set; }
    public string? Oid1 { get; set; }
    public string? Oid2 { get; set; }
    public string? Oid3 { get; set; }
    public string? Oid4 { get; set; }
    public string? Oid5 { get; set; }
    public string? Oid6 { get; set; }
    public string? Oid7 { get; set; }
    public string? Oid8 { get; set; }
    public string? Oid9 { get; set; }
    public string? Oid10 { get; set; }
    public int? OidIndex { get; set; }

    internal bool IsContinuousData()
    {
        switch (ServiceMode)
        {
            case 40:
            case 45:
                {
                    if (OidDetail == 30)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            case 101:
            case 102:
            case 106:
            case 107:
                return true;
            default:
                return false;
        }
    }
}
*/

public enum eSnmpVersion
{
    v1 = 10
        , v2c = 20
        , v3 = 30
}

public enum eSnmpAuthProtocol
{
    MD5 = 10
    , SHA = 20
}

public enum eSnmpPrivacyProtocol
{
    DES = 10
    , IDEA = 20
    , AES128 = 30
    , AES192 = 40
    , AES256 = 50
    , TripleDES = 60
}

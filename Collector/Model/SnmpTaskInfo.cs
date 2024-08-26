using System.Collections.Concurrent;
using SnmpSharpNet;

public class SendDataModel
{
    public string? CollectorToken { get; set; }
    public List<SnmpTaskInfo>? SnmpLogDataList { get; set; }
}

public class SnmpTaskInfo
{
    /// <summary>
    /// 110: windows, 120: linux
    /// </summary>
    /// <value></value>
    public int? DeviceType { get; set; }
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
    public string? IpForwarding { get; set; }

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
    public List<SnmpLog> snmpLogList { get; set; } = new List<SnmpLog>();
    public List<string> ArpList { get; set; } = new List<string>();
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
    public string? ScanCidr { get; set; }
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

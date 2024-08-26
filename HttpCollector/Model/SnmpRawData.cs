using System.Collections.Concurrent;
using NLog;


public class SnmpReturnModule
{
    public string? CollectorDataKey { get; set; }
    public int? DeviceNo { get; set; }
    public int? DataKind { get; set; }
    public int? SnmpCollectorNo { get; set; }
    public double? Total { get; set; }
    public double? Used { get; set; }
    public string? GetValue { get; set; }
    public string? DataName { get; set; }
    public DateTimeOffset Tdate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public int? Status { get; set; } //40: 連不到設備，80: 連線正常
    public int? DevServiceNo { get; set; }// FK
    public string? RevisedUrl { get; set; }
}

public class SnmpReturnOidModule
{
    public int? DeviceNo { get; set; }
    public int DataKind { get; set; }
    public List<OidReturnModule> InterfaceList { get; set; } = new List<OidReturnModule>();
}

public class SnmpDeviceInfoModule
{
    public int? DeviceNo { get; set; }
    public int DataKind { get; set; }
    public string? DataKindValue { get; set; }
}

class DeviceData
{
    private static Logger logger = LogManager.GetCurrentClassLogger();
    public string? DeviceID { get; set; }
    public int DeviceType { get; set; }
    public string? DeviceIp { get; set; }
    public ConcurrentDictionary<string, List<RawData>> SnmpDataObj { get; set; } = new ConcurrentDictionary<string, List<RawData>>();
    // public ConcurrentDictionary<int, List<RawData>> PingDataObj { get; set; } = new ConcurrentDictionary<int, List<RawData>>();
    // public ConcurrentDictionary<int, List<RawData>> WaaDataObj { get; set; } = new ConcurrentDictionary<int, List<RawData>>();

    public class RawData
    {
        public string? CollectorDataKey { get; set; }
        public int? OidDetail { get; set; }
        public DateTime Tdate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime? LastTdate { get; set; }
        public List<string?> Data { get; set; } = new List<string?>();
        // public int? TcpPort { get; set; }
        // public int? TcpStatus { get; set; }
        public double? TcpLatency { get; set; }
        public double? TransitBps { get; set; }
        public string? DataName { get; set; }
        public List<OidReturnModule> InterfaceList { get; set; } = new List<OidReturnModule>();
        public List<OidReturnModule> StorageInterfaceList { get; set; } = new List<OidReturnModule>();
        // public PingValue? PingValue { get; set; }
        // public PingValue? TCPingValue { get; set; }
        // public int? DevServiceNo { get; set; }
        public int? DataKind { get; set; }
        // public string? RevisedUrl { get; set; }
        public List<WaaResultModel> WaaResultList { get; set; } = new List<WaaResultModel>();
    }

}

public class OidReturnModule
{
    public DateTimeOffset? Tdate { get; set; }
    public int? OidType { get; set; }
    public int? InterfaceStatus { get; set; }
    public int? InterfaceType { get; set; }
    public string? InterfaceMac { get; set; }
    public string? InterfaceIp { get; set; }
    public string? OidName { get; set; }
    public string? Oid { get; set; }
    public string? OidIndex { get; set; }
    public double? Value { get; set; }
    public double? Size { get; set; }
    public double? Total { get; set; }
    public double? Used { get; set; }
    public double? IfInBype { get; set; }//test
    public double? IfOutBype { get; set; }//test
}

/// <summary>
/// 計算 Ping 數值
/// </summary>
public class PingValue
{
    /// <summary>
    /// 已傳送
    /// </summary>
    public int SentNum { get; set; } = 0;

    /// <summary>
    /// 已收到
    /// </summary>
    public int ReceivedNum { get; set; } = 0;

    /// <summary>
    /// 已遺失
    /// </summary>
    public int LostNum { get; set; } = 0;

    /// <summary>
    /// Ping回應時間
    /// </summary>
    public List<int> PingTimeList { get; set; } = new List<int>();

    /// <summary>
    /// 最小值
    /// </summary>
    public int? MinValue { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    public int? MaxValue { get; set; }

    /// <summary>
    /// 平均值
    /// </summary>
    public int? AvgValue { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public int SuccessRate { get; set; } = 0;

}
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Timers;
using Collector.Common;
using Collector.Snmp;
using HttpCollector.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using NLog;
using SnmpSharpNet;

class Program
{
    private static Logger logger = LogManager.GetCurrentClassLogger();
    private static string collectorToken = "HttpCollector";
    private static string websocketUrl = "https://localhost:5443/ApiWebSocketHub";
    private static string apiUrlBase = "https://localhost:5443";
    private static int ScanThreadCount = 255;
    private static HubConnection connection;
    public static Random rdm = new Random();
    private static object locker = new Object();
    private static HttpClientHandler handler = new HttpClientHandler()
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
    private static readonly HttpClient apiHttpClient = new HttpClient(handler);

    static async Task Main(string[] args)
    {
        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} ***********************************");
        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} **           Collector");
        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} **           Ver: 202404251424");
        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} ***********************************");

        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);


        // Thread Pool 至少準備幾個 Thread
        ThreadPool.SetMinThreads(Environment.ProcessorCount * 10, minCompletionPortThreads);

        ServicePointManager.DefaultConnectionLimit = Int32.MaxValue;

        // Http SSL 設定
        ServicePointManager.ServerCertificateValidationCallback = delegate
        {
            return true;
        };
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13 | SecurityProtocolType.Tls;

        connection = new HubConnectionBuilder()
            .WithUrl(websocketUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3) })
            .Build();

        // 顯示 Hub 傳入的訊息
        connection.On<string>("ReceiveMessage", message =>
        {
            Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} {message}");
        });

        connection.Reconnected += async (item) =>
        {
            await connection.InvokeAsync("register", "HttpCollector");
        };

        connection.Closed += async (error) =>
        {
            Console.WriteLine("Connection closed. Error: " + error?.Message);

            WebSocketConnAsync();
        };

        WebSocketConnAsync();

        // ScanHttp
        connection.On<string>("ScanHttp", async message =>
        {
            try
            {
                ScanHttpParam httpMission = JsonConvert.DeserializeObject<ScanHttpParam>(message);

                WaaResultModel httpRes = null;

                Dictionary<string, string> headers = new Dictionary<string, string>();
                HttpContent content = null;

                if (httpMission.HttpMethod == 10)
                {
                    httpRes = await HttpFunction.GetUrl(HttpMethod.Get, httpMission.FQDN, httpMission);
                }
                else if (httpMission.HttpMethod == 20)
                {
                    if (!string.IsNullOrEmpty(httpMission.HttpHeader))
                    {
                        headers = JsonConvert.DeserializeObject<Dictionary<string, string>>(httpMission.HttpHeader);
                    }
                    if (!string.IsNullOrEmpty(httpMission.HttpContent))
                    {
                        content = new StringContent(httpMission.HttpContent, System.Text.Encoding.UTF8, "application/json");
                    }

                    httpRes = await HttpFunction.GetUrl(HttpMethod.Post, httpMission.FQDN, httpMission, headers, content);
                }


                // 結果回傳到 API
                if (httpRes != null)
                {
                    httpRes.MissionId = httpMission.MissionId;

                    SendDataModel sendDataModel = new SendDataModel();
                    sendDataModel.CollectorToken = collectorToken;
                    sendDataModel.HttpLogDataList = new List<WaaResultModel>();
                    sendDataModel.HttpLogDataList.Add(httpRes);

                    string errorMsg = await SendHttpLog(sendDataModel);
                    if (!string.IsNullOrEmpty(errorMsg))
                    {
                        Console.WriteLine(errorMsg);
                    }
                }

                // 通知完成
                connection.InvokeAsync("ScanHttpFinish", httpMission.MissionId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }


        });

        // 啟動 Timer
        System.Timers.Timer timer = new System.Timers.Timer(60 * 1000);
        timer.Elapsed += TimerElapsed;
        timer.Start();

        // Console.WriteLine("Press any key to exit...");
        // Console.ReadLine();

        while (true)
        {
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// /// websocket conn
    /// </summary>
    /// <returns></returns>
    private static async Task WebSocketConnAsync()
    {
        while (true)
        {
            await Task.Delay(1000);
            try
            {
                await connection.StartAsync();

                await connection.InvokeAsync("register", "HttpCollector");

                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"websocket connection failed: {websocketUrl}, {ex.GetBaseException().Message}");
            }
        }
    }


    private static async Task<ConcurrentDictionary<string, List<DeviceData.RawData>>> ScanHttp(SnmpTaskInfo snmpDevice, List<DeviceServiceModule> oidList
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

                        await Task.Delay(rdm.Next(1, 10));
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
                                //...計算流量差值
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
                        oidDetailDict.InterfaceList = await snmpFunc.OidGetBulk_InterfaceOrStorage(snmpParam, oidInfo);

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
                        oidDetailDict.StorageInterfaceList = await snmpFunc.OidGetBulk_InterfaceOrStorage(snmpParam, oidInfo);

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
                        oidDetailDict.Data = await snmpFunc.OidGetBulk(snmpParam, oidInfo);
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
    private static async Task<List<string?>> ScanInterfaceDetail(SnmpTaskInfo device, DeviceServiceModule oidInfo)
    {
        List<string?> snmpValueList = new List<string?>();

        try
        {
            var OidList = new List<DeviceServiceModule>();
            OidList.Add(oidInfo);

            // Call SNMP
            ConcurrentDictionary<string, List<DeviceData.RawData>> _snmpRawData = await ScanHttp(device, OidList, isRealTimeTrafficEnd: 90, fullDetect: false);

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

    private static readonly SemaphoreSlim Locker_asyncScanLan = new SemaphoreSlim(1, 1);
    private static async void TimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // 限制只能一個 thread 執行
        if (!await Locker_asyncScanLan.WaitAsync(0))
        {
            return;
        }

        Console.WriteLine($"Run timer");

        try
        {
            ScanHttpParam httpMission = new ScanHttpParam();
            httpMission.ParamDefault();
            httpMission.MissionId = Guid.NewGuid().ToString();
            httpMission.HttpMethod = 10;
            httpMission.HttpContent = null;
            httpMission.HttpHeader = null;

            if (Tool.CreateUri("http://www.google.com", out Uri uri, out string sUri))
            {
                httpMission.FQDN = sUri;
            }

            WaaResultModel httpRes = null;

            Dictionary<string, string> headers = new Dictionary<string, string>();
            HttpContent content = null;

            if (httpMission.HttpMethod == 10)
            {
                httpRes = await HttpFunction.GetUrl(HttpMethod.Get, httpMission.FQDN, httpMission);
            }
            else if (httpMission.HttpMethod == 20)
            {
                if (!string.IsNullOrEmpty(httpMission.HttpHeader))
                {
                    headers = JsonConvert.DeserializeObject<Dictionary<string, string>>(httpMission.HttpHeader);
                }
                if (!string.IsNullOrEmpty(httpMission.HttpContent))
                {
                    content = new StringContent(httpMission.HttpContent, System.Text.Encoding.UTF8, "application/json");
                }

                httpRes = await HttpFunction.GetUrl(HttpMethod.Post, httpMission.FQDN, httpMission, headers, content);
            }

            // 結果回傳到 API
            if (httpRes != null)
            {
                List<WaaResultModel> dataList = new List<WaaResultModel>();
                dataList.Add(httpRes);
                string errorMsg = await AddHttpLog(dataList);
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    throw new Exception(errorMsg);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        finally
        {
            Locker_asyncScanLan.Release();
        }

        Console.WriteLine($"Timer finish");
    }

    private static async Task<string> AddHttpLog(List<WaaResultModel> dataList)
    {
        string errorMsg = string.Empty;

        string jsonObject = JsonConvert.SerializeObject(dataList);
        HttpContent content = new StringContent(jsonObject, Encoding.UTF8, "application/json");

        string URL = $"{apiUrlBase}/api/Http/AddHttpLog";

        try
        {
            var response = await apiHttpClient.PostAsync(URL, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            errorMsg = $"{ex.GetBaseException().Message} => {URL}";
        }

        return errorMsg;
    }

    private static async Task<string> SendHttpLog(SendDataModel data)
    {
        string errorMsg = string.Empty;

        string jsonObject = JsonConvert.SerializeObject(data);
        HttpContent content = new StringContent(jsonObject, Encoding.UTF8, "application/json");

        string URL = $"{apiUrlBase}/api/Http/ReceiveHttpLog";

        try
        {
            var response = await apiHttpClient.PostAsync(URL, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            errorMsg = $"{ex.GetBaseException().Message} => {URL}";
        }

        return errorMsg;
    }

}


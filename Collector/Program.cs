using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Timers;
using Collector.Common;
using Collector.Snmp;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using NLog;
using SnmpSharpNet;

class Program
{
    private static Logger logger = LogManager.GetCurrentClassLogger();
    private static string collectorToken = "Collector";
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
        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} **           Ver: 202406071456");
        Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} ***********************************");

        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);


        // Thread Pool 至少準備幾個 Thread
        ThreadPool.SetMinThreads(300, minCompletionPortThreads);

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
            await connection.InvokeAsync("register", "Collector");
        };

        connection.Closed += async (error) =>
        {
            Console.WriteLine("Websocket closed. Error: " + error?.Message);

            WebSocketConnAsync();
        };

        WebSocketConnAsync();

        // ScanDevice
        connection.On<string>("ScanDevice", async message =>
        {
            try
            {
                Console.WriteLine($"ScanDevice: {message}");
                List<SnmpTaskInfoMission> missionList = JsonConvert.DeserializeObject<List<SnmpTaskInfoMission>>(message);

                string? missionId = string.Empty;
                if (missionList.Count > 0)
                {
                    missionId = missionList[0].MissionId;
                }

                // missionList = missionList.Where(x => x.DeviceIp == "10.88.21.196").ToList();//test

                SnmpMission snmpMission = new SnmpMission();
                List<SnmpTaskInfo> snmpRawDataList = await snmpMission.RunSnmpMission(missionList);

                // 結果回傳到 API
                SendDataModel sendDataModel = new SendDataModel();
                sendDataModel.CollectorToken = collectorToken;
                sendDataModel.SnmpLogDataList = snmpRawDataList;

                string errorMsg = await SendSnmpLog(sendDataModel);
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    Console.WriteLine(errorMsg);
                }

                // 通知完成
                connection.InvokeAsync("ScanDeviceFinish", missionId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }


        });

        // 啟動 Timer
        // System.Timers.Timer timer = new System.Timers.Timer(15 * 1000);
        // timer.Elapsed += TimerElapsed;
        // timer.Start();

        // Console.WriteLine("Press any key to exit...");
        // Console.ReadLine();

        while (true)
        {
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// websocket conn
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

                await connection.InvokeAsync("register", "Collector");

                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"websocket connection failed: {websocketUrl}, {ex.GetBaseException().Message}");
            }
        }
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
            List<SnmpTaskInfoMission> missionList = new List<SnmpTaskInfoMission>();
            SnmpTaskInfoMission mission = new SnmpTaskInfoMission();
            mission.MissionId = Guid.NewGuid().ToString();
            mission.Community = "public";
            mission.SnmpPort = 161;
            mission.GetTraffic = 80;
            mission.ServiceList = SnmpMission.CreateSensorList();
            missionList.Add(mission);

            SnmpMission snmpMission = new SnmpMission();
            List<SnmpTaskInfo> snmpRawDataList = await snmpMission.RunSnmpMission(missionList);

            if (snmpRawDataList.Count > 0)
            {
                string errorMsg = await AddSnmpLog(snmpRawDataList);
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

    private static async Task<string> AddSnmpLog(List<SnmpTaskInfo> dataList)
    {
        string errorMsg = string.Empty;

        string jsonObject = JsonConvert.SerializeObject(dataList);
        HttpContent content = new StringContent(jsonObject, Encoding.UTF8, "application/json");

        string URL = $"{apiUrlBase}/api/Snmp/AddSnmpLog";

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

    private static async Task<string> SendSnmpLog(SendDataModel data)
    {
        string errorMsg = string.Empty;

        string jsonObject = JsonConvert.SerializeObject(data);
        HttpContent content = new StringContent(jsonObject, Encoding.UTF8, "application/json");

        string URL = $"{apiUrlBase}/api/Snmp/ReceiveSnmpLog";

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


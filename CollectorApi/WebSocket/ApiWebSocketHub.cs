using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using static CollectorApi.Controllers.Collector.SnmpController;

namespace CollectorApi.WebSocket
{
    public class ApiWebSocketHub : Hub
    {
        private readonly IHubContext<ApiWebSocketHub> _hubContext;

        public ApiWebSocketHub(IHubContext<ApiWebSocketHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // 保存 Client 識別資料
        public class ClientInfo
        {
            public string? ConnId { get; set; }
            public string? CollectorToken { get; set; }
            // public int CollectorNo { get; set; }
            /// <summary>
            /// key: missionId
            /// </summary> <summary>
            /// 
            /// </summary>
            /// <typeparam name="string"></typeparam>
            /// <typeparam name="Mission"></typeparam>
            /// <returns></returns>
            public ConcurrentDictionary<string, Mission> Mission { get; set; } = new ConcurrentDictionary<string, Mission>();
        }

        private static ConcurrentDictionary<string, TaskCompletionSource<string>> _waitingTasks = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        public class Mission
        {
            public int Status { get; set; } //80: Run, 90: Finish
            public DateTime CrtDate { get; set; }
            public List<object> ResponseDataList { get; set; } = new List<object>();
        }

        /// <summary>
        /// key: collectorToken
        /// </summary>
        /// <typeparam name="string"></typeparam>
        /// <typeparam name="ClientInfo"></typeparam>
        /// <returns></returns>
        public static Dictionary<string, ClientInfo> CurrClients = new Dictionary<string, ClientInfo>();

        public override Task OnConnectedAsync()
        {
            Console.WriteLine("WebSocket connected: " + Context.ConnectionId);

            return base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"WebSocket disconnected: {Context.ConnectionId}.");

            string cid = Context.ConnectionId;
            string collectorToken = string.Empty;

            // 刪除連線資料
            foreach (var item in CurrClients)
            {
                var conn = item.Value;

                if (conn.ConnId != cid)
                {
                    continue;
                }

                collectorToken = conn.CollectorToken;

                break;
            }
            if (!string.IsNullOrEmpty(collectorToken))
            {
                CurrClients.Remove(collectorToken);
            }

            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }

        // 註冊 Client 識別名稱
        public void Register(string collectorToken)
        {
            string cid = Context.ConnectionId;

            lock (CurrClients)
            {
                if (CurrClients.ContainsKey(collectorToken))
                {
                    CurrClients.Remove(collectorToken);
                }
                CurrClients[collectorToken] = new ClientInfo()
                {
                    ConnId = cid,
                    CollectorToken = collectorToken
                };

                // 傳訊息到 collector
                Clients.Client(cid).SendAsync("ReceiveMessage", $"Cnnect websocket sucessfully.");
            }
        }

        /*
                public async Task ScanDeviceRes(string jData)
                {
                    Console.WriteLine($"ScanDeviceRes: {jData}");

                    string cid = Context.ConnectionId;
                    string collectorToken = string.Empty;

                    var snmpRes = JsonConvert.DeserializeObject<SnmpTaskInfo>(jData);

                    // Collector 回傳結果寫入記憶體
                    foreach (var item in CurrClients)
                    {
                        var conn = item.Value;

                        if (conn.ConnId != cid)
                        {
                            continue;
                        }

                        collectorToken = conn.CollectorToken;

                        if (item.Value.Mission.TryGetValue(snmpRes.MissionId, out Mission mission))
                        {
                            mission.ResponseDataList.Add(snmpRes);
                        }

                        break;
                    }
                }
                */

        public async Task ScanDeviceFinish(string missionId)
        {
            Console.WriteLine($"ScanDeviceFinish: {missionId}");

            string cid = Context.ConnectionId;
            string collectorToken = string.Empty;

            // 更新任務狀態
            foreach (var item in CurrClients)
            {
                var conn = item.Value;

                if (conn.ConnId != cid)
                {
                    continue;
                }

                collectorToken = conn.CollectorToken;

                if (item.Value.Mission.TryGetValue(missionId, out Mission mission))
                {
                    mission.Status = 90;
                }

                break;
            }
        }

        /*
                public async Task ScanHttpRes(string jData)
                {
                    Console.WriteLine($"ScanHttpRes: {jData}");

                    string cid = Context.ConnectionId;
                    string collectorToken = string.Empty;

                    var snmpRes = JsonConvert.DeserializeObject<WaaResultModel>(jData);

                    // Collector 回傳結果寫入記憶體
                    foreach (var item in CurrClients)
                    {
                        var conn = item.Value;

                        if (conn.ConnId != cid)
                        {
                            continue;
                        }

                        collectorToken = conn.CollectorToken;

                        if (item.Value.Mission.TryGetValue(snmpRes.MissionId, out Mission mission))
                        {
                            mission.ResponseDataList.Add(snmpRes);
                        }

                        break;
                    }
                }
                */

        public async Task ScanHttpFinish(string missionId)
        {
            Console.WriteLine($"ScanHttpFinish: {missionId}");

            string cid = Context.ConnectionId;
            string collectorToken = string.Empty;

            // 更新任務狀態
            foreach (var item in CurrClients)
            {
                var conn = item.Value;

                if (conn.ConnId != cid)
                {
                    continue;
                }

                collectorToken = conn.CollectorToken;

                if (item.Value.Mission.TryGetValue(missionId, out Mission mission))
                {
                    mission.Status = 90;
                }

                break;
            }
        }
    }
}
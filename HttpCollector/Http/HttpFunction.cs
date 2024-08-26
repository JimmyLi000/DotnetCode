

using System.Diagnostics;
using System.Net;
using Collector.Common;

namespace HttpCollector.Http
{
    class HttpFunction
    {
        private static readonly SemaphoreSlim Locker_asyncGetUrl = new SemaphoreSlim(1);
        public static async Task<WaaResultModel> GetUrl(HttpMethod method, string url, ScanHttpParam httpParam
                    , Dictionary<string, string> headers = null, HttpContent content = null)
        {
            // 每個 Request 之間加一點延遲，避免瞬間大量 Request 導致 Timeout
            await Locker_asyncGetUrl.WaitAsync();
            await Task.Delay(10);
            Locker_asyncGetUrl.Release();

            var waaResult = new WaaResultModel();

            // 本機訊息
            waaResult.DeviceHostName = Dns.GetHostName();
            // 過濾掉容器虛擬的 ip
            waaResult.DeviceAddressList = (await Tool.GetLocalAddress()).Select(ip => ip.ToString()).Where(ip => !ip.EndsWith(".0.1")).ToList();

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                httpClient.Timeout = TimeSpan.FromMilliseconds((double)httpParam.HttpTimeoutMs);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");

                //headers
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                waaResult.StartTime = DateTime.Now;

                // 建立URI，將網址加上 Http://.. 的格式，只有任務才需 TCPing
                if (Tool.CreateUri(url, out Uri uri, out url))
                {

                }

                //偵測開網站花費時間
                Stopwatch sw = new Stopwatch();

                HttpResponseMessage getResponse = null;

                try
                {
                    // DNS 花費時間
                    string FQDN_IP = null;
                    if (Tool.isIP(uri.IdnHost))
                    {
                        FQDN_IP = uri.IdnHost;
                    }
                    else
                    {
                        Stopwatch dnsTime = new Stopwatch();
                        dnsTime.Start();
                        string ip = await Tool.FqdnToIpv4(uri.IdnHost);
                        dnsTime.Stop();

                        waaResult.DNSResolveTime = (int)dnsTime.ElapsedMilliseconds < 1 ? 1 : (int)dnsTime.ElapsedMilliseconds;

                        if (Tool.isIP(ip))
                        {
                            FQDN_IP = ip;
                        }
                    }
                    waaResult.FQDN_IP = FQDN_IP;

                    // 計時
                    sw.Start();

                    //TCP 連線
                    bool tcpConn = await Tool.TcpConnectionAsync(FQDN_IP, uri.Port, httpParam.PingTimeoutMs.Value);
                    sw.Stop();
                    if (tcpConn)
                    {
                        waaResult.TCPTime = (int)sw.ElapsedMilliseconds < 1 ? 1 : (int)sw.ElapsedMilliseconds;
                    }

                    sw.Restart();

                    if (tcpConn)
                    {
                        if (method == HttpMethod.Post)
                        {
                            getResponse = await httpClient.PostAsync(url, content);

                            waaResult.DownloadSize = getResponse.Content.Headers.ContentLength.HasValue ? getResponse.Content.Headers.ContentLength.Value * 8 : getResponse.Content.Headers.ContentLength;
                        }
                        else
                        {
                            //只讀Header
                            getResponse = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        }

                        waaResult.HttpStatus = (int)getResponse.StatusCode;
                        waaResult.Redirect_FQDN = getResponse.RequestMessage.RequestUri.OriginalString != url ? getResponse.RequestMessage.RequestUri.AbsoluteUri : null;
                        waaResult.FileSize = getResponse.Content.Headers.ContentLength.HasValue ? getResponse.Content.Headers.ContentLength.Value * 8 : getResponse.Content.Headers.ContentLength;

                        if (getResponse != null && method == HttpMethod.Get)
                        {
                            //下載(讀取資料流)
                            var stream = await getResponse.Content.ReadAsStreamAsync();
                            await DownloadStream(stream, httpParam, waaResult);
                            stream.Close();
                            stream.Dispose();
                            getResponse.Dispose();
                        }
                    }

                }
                catch (Exception ex)
                {

                }

                sw.Stop();

                //如果下載不到資料，就將 HttpStatus 清除當作失敗
                if (!waaResult.DownloadSize.HasValue)
                {
                    waaResult.Redirect_FQDN = null;
                    waaResult.FileSize = null;
                }
                else
                {
                    //開網站花費時間
                    int? downloadTime = sw.ElapsedMilliseconds < 1 ? 1 : (int)sw.ElapsedMilliseconds;
                    waaResult.DownloadTime = downloadTime;

                    //計算下載速率 (Mbps)
                    double? Transit = null;
                    Transit = Math.Ceiling(waaResult.DownloadSize.Value / ((double)downloadTime / (double)1024));
                    waaResult.DownloadSpeed = (long)Transit;
                }

                waaResult.FQDN = url;

                //計算總花費時間
                if (waaResult.HttpStatus.HasValue)
                {
                    int? totalTime = 0;

                    totalTime += waaResult.DNSResolveTime.HasValue ? waaResult.DNSResolveTime : 0;
                    totalTime += waaResult.DownloadTime.HasValue ? waaResult.DownloadTime : 0;
                    totalTime += waaResult.TCPTime.HasValue ? waaResult.TCPTime : 0;
                    waaResult.TotalTime = totalTime != 0 ? totalTime : null;
                }

            }

            waaResult.EndTime = DateTime.Now;

            return waaResult;
        }

        // HTTP 下載
        private static async Task<WaaResultModel> DownloadStream(Stream stream, ScanHttpParam _waaParam, WaaResultModel _waaResult)
        {
            using (var MemStream = new MemoryStream())
            {
                var tempBuffer = new byte[1024];
                int bytesRead;

                long MaxDownloadSize = (long)_waaParam.MaxDownloadSize;

                while ((bytesRead = await stream.ReadAsync(tempBuffer, 0, tempBuffer.Length)) != 0)
                {
                    await MemStream.WriteAsync(tempBuffer, 0, bytesRead);

                    //限制下載大小
                    if (MemStream.Length >= MaxDownloadSize)
                    {
                        break;
                    }
                }

                //已下載大小
                _waaResult.DownloadSize = MemStream.Length;

                MemStream.Flush();
            }

            //單位轉換為 bit
            _waaResult.DownloadSize = _waaResult.DownloadSize.HasValue ? _waaResult.DownloadSize * 8 : _waaResult.DownloadSize;

            return _waaResult;
        }
    }
}
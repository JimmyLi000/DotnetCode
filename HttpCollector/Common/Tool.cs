using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Collector.Common
{
    public class Tool
    {
        private static string hostName = Dns.GetHostName();

        /// <summary>
        /// Parse SNMP hexadecimal time
        /// </summary>
        /// <returns></returns>
        public static string ParseSnmpTime(string str)
        {
            string result = string.Empty;

            try
            {
                if (!string.IsNullOrEmpty(str))
                {
                    var aryTime = str
                            .Split(' ')
                            .Select(s => byte.Parse(s, NumberStyles.HexNumber))
                            .ToArray();

                    int year = aryTime[0] * 256 + aryTime[1];
                    int month = aryTime[2];
                    int day = aryTime[3];
                    int hour = aryTime[4];
                    int min = aryTime[5];
                    int sec = aryTime[6];
                    int deciSeconds = aryTime[7]; //分秒
                    int utcDirection = 0;    //ASCII CHR 43: +, 45: -
                    int utcHour = 0;
                    int utcMin = 0;

                    result = String.Format("{0}/{1,2:00}/{2,2:00} {3,2:00}:{4,2:00}:{5,2:00}", year, month, day, hour, min, sec);

                    //若有 UTC 參數
                    if (aryTime.Length == 11)
                    {
                        utcDirection = aryTime[8];    //ASCII CHR 43: +, 45: -
                        utcHour = aryTime[9];
                        utcMin = aryTime[10];

                        if (DateTime.TryParse(result, out DateTime time))
                        {
                            //若有 UTC 參數，強制轉為 +08:00 時區
                            if (utcDirection == 43)
                            {
                                //時區方向: +
                                if (utcHour != 8)
                                {
                                    time.AddHours(8 - utcHour);
                                }
                            }
                            else if (utcDirection == 45)
                            {
                                //時區方向: -
                                if (utcHour != 8)
                                {
                                    time.AddHours(8 + utcHour);
                                }
                            }
                        }

                        result = $"{time.ToString("yyyy/MM/dd HH:mm:ss")}(UTC)";
                    }

                }
            }
            catch
            {
                // logger.Error(e);
            }

            return result;
        }

        /// <summary>
        /// 建立URI. true: 成功，false: 失敗
        /// </summary>
        public static bool CreateUri(string sURI, out Uri outURI, out string outsURI)
        {
            //解碼後用於HttpWebRequest的URI
            outsURI = string.Empty;

            try
            {
                //移除頭尾空白
                sURI = sURI.Trim();

                bool b = Uri.IsWellFormedUriString(sURI, UriKind.Absolute);

                //若網址沒有Scheme就加入http://
                if (sURI.StartsWith("http://") == false && sURI.StartsWith("https://") == false)
                {
                    if (sURI.Contains(":443"))
                    {
                        sURI = "https://" + sURI;
                    }
                    else
                    {
                        sURI = "http://" + sURI;
                    }
                }

                //判斷並建立URI
                bool bUriResult = Uri.TryCreate(sURI, UriKind.Absolute, out outURI) && outURI.IsWellFormedOriginalString();

                //若判斷URI無法使用
                if (bUriResult == false)
                {
                    outURI = null;

                    return false;
                }

                //解碼後用於HttpWebRequest的Domain
                outsURI = outURI.Scheme + "://" + outURI.IdnHost;

                //網址是否帶Port號
                if (sURI.IndexOf(":80") != -1)
                {
                    outsURI += ":80";
                }
                else if (sURI.IndexOf(":443") != -1)
                {
                    outsURI += ":443";
                }
                else if (outURI.Port != 80 && outURI.Port != 443)
                {
                    outsURI += ":" + outURI.Port.ToString();
                }

                //加入path
                outsURI += outURI.PathAndQuery;

                //不加入最後的/斜線
                if (outsURI.EndsWith("/"))
                {
                    outsURI = outsURI.Substring(0, (outsURI.Length - 1));
                }

                return true;
            }
            catch (Exception ex)
            {
                outURI = null;

                return false;
            }
        }

        /// <summary>
        /// 檢查是否為IP. true: 是，false: 否
        /// </summary>
        /// <param name="IP"></param>
        /// <returns></returns>
        public static bool isIP(string IP)
        {
            try
            {
                if (IPAddress.TryParse(IP, out IPAddress newIP))
                {
                    if (newIP.ToString() == IP)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 將網址轉為 IPv4
        /// </summary>
        /// <param name="FQDN"></param>
        /// <returns></returns>
        public static async Task<string> FqdnToIpv4(string FQDN)
        {
            string Ip = string.Empty;
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(FQDN);

                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Ip = address.ToString();
                        break;
                    }
                }
            }
            catch
            {

            }

            return Ip;
        }

        /// <summary>
        /// TCP 連線
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="port"></param>
        /// <param name="timeoutMilliseconds"></param>
        /// <returns></returns>
        public static async Task<bool> TcpConnectionAsync(string hostname, int port, int timeoutMilliseconds = 1000)
        {
            using (var tcpClient = new TcpClient())
            {
                try
                {
                    var connectTask = tcpClient.ConnectAsync(hostname, port);
                    var timeoutTask = Task.Delay(timeoutMilliseconds);
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

                    if (completedTask == timeoutTask)
                    {
                        return false;
                    }

                    await connectTask;
                    return tcpClient.Connected;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    tcpClient.Close();
                }
            }
        }

        /// <summary>
        /// 取得本機 IP
        /// </summary>
        /// <returns></returns>
        public static async Task<List<IPAddress>> GetLocalAddress()
        {
            List<IPAddress> addressList = new List<IPAddress>();

            IPHostEntry hostEntry = await Dns.GetHostEntryAsync(hostName);

            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    addressList.Add(ip);
                }
            }

            addressList = addressList
                .OrderBy(ip => ip.ToString())
                .ToList();

            return addressList;
        }


    }
}
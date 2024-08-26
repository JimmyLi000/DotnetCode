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
        /// 解析 IP 範圍
        /// </summary>
        /// <param name="IpRangeStart"></param>
        /// <param name="IpRangeEnd"></param>
        /// <returns></returns>
        public static List<string> ParseIpRange(string IpRangeStart, string IpRangeEnd)
        {
            List<string> resultIpList = new List<string>();

            List<string> ipRangeStartBit = IpRangeStart.Split('.').ToList();
            List<string> ipRangeEndBit = IpRangeEnd.Split('.').ToList();

            int start = BitConverter.ToInt32(new byte[] { byte.Parse(ipRangeStartBit[3]), byte.Parse(ipRangeStartBit[2]), byte.Parse(ipRangeStartBit[1]), byte.Parse(ipRangeStartBit[0]) }, 0);
            int end = BitConverter.ToInt32(new byte[] { byte.Parse(ipRangeEndBit[3]), byte.Parse(ipRangeEndBit[2]), byte.Parse(ipRangeEndBit[1]), byte.Parse(ipRangeEndBit[0]) }, 0);
            for (int i = start; i <= end; i++)
            {
                byte[] bytes = BitConverter.GetBytes(i);
                resultIpList.Add(new IPAddress(new[] { bytes[3], bytes[2], bytes[1], bytes[0] }).ToString());
            }

            return resultIpList;
        }

        /// <summary>
        /// 產生整段 ip
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public static List<string> GenerateSubnetIps(string ip)
        {
            List<string> subnetIps = new List<string>();
            var addressBytes = IPAddress.Parse(ip).GetAddressBytes();
            addressBytes[3] = 0; // Set the last part of the IP to 0

            for (int i = 1; i < 255; i++) // Start from 1 to exclude the network address
            {
                addressBytes[3] = (byte)i;
                subnetIps.Add(new IPAddress(addressBytes).ToString());
            }

            return subnetIps;
        }

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

        private static readonly SemaphoreSlim Locker_TCPingAsync = new SemaphoreSlim(300);
        /// <summary>
        /// TCPing
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> TCPingAsync(string host, int port, int timeout = 1000)
        {
            bool isOpen = false;

            try
            {
                if (await Locker_TCPingAsync.WaitAsync(600000).ConfigureAwait(false))
                {
                    using (TcpClient tcpClient = new TcpClient())
                    {
                        var tokenSource = new CancellationTokenSource();
                        CancellationToken ct = tokenSource.Token;

                        var connectTask = tcpClient.ConnectAsync(host, port);
                        var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeout, ct)).ConfigureAwait(false);

                        if (completedTask == connectTask && tcpClient.Connected)
                        {
                            // connect success
                            isOpen = true;
                        }
                        tokenSource.Cancel();
                        tcpClient.Close();
                    }
                }
            }
            catch
            {

            }
            finally
            {
                Locker_TCPingAsync.Release();
            }

            return isOpen;
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
                    Console.WriteLine($"LocalIP Address: {ip}");
                }
            }

            addressList = addressList
                .OrderBy(ip => ip.ToString())
                .ToList();

            return addressList;
        }

    }
}
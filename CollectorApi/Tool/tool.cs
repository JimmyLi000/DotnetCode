using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using static CollectorApi.Controllers.Collector.SnmpController;

namespace CollectorApi.Tool
{
    public class tool
    {
        protected static Logger logger { get; private set; }

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
        /// 建立URI. true: 成功，false: 失敗
        /// </summary>
        public static bool CreateUri(string sURI, out Uri outURI, out string outsURI)
        {
            //解碼後用於HttpWebRequest的URI
            outsURI = string.Empty;

            try
            {
                if (sURI == null)
                {
                    outURI = null;
                    return false;
                }

                //移除頭尾空白
                sURI = sURI.Trim();

                bool b = Uri.IsWellFormedUriString(sURI, UriKind.Absolute);

                // 若網址沒有 Scheme 就加入 http://
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

                // 判斷並建立 URI
                bool bUriResult = Uri.TryCreate(sURI, UriKind.Absolute, out outURI) && outURI.IsWellFormedOriginalString();

                // 若判斷 URI 無法使用
                if (bUriResult == false)
                {
                    outURI = null;

                    return false;
                }

                // 解碼後用於HttpWebRequest的Domain
                outsURI = outURI.Scheme + "://" + outURI.IdnHost;

                // 網址帶 Port 號
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

                // 加入 path
                outsURI += outURI.PathAndQuery;

                // 移除 path 最後一個斜線
                if (outsURI.EndsWith("/"))
                {
                    outsURI = outsURI.Substring(0, (outsURI.Length - 1));
                }

                return true;
            }
            catch (Exception)
            {
                outURI = null;

                return false;
            }
        }

        /// <summary>
        /// 解析 JSON
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, object> DeserializeJson(dynamic dJson)
        {
            Dictionary<string, object> obj = null;
            try
            {
                string json = dJson.ToString();
                if (!string.IsNullOrEmpty(json))
                {
                    obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                }
            }
            catch
            {
            }
            return obj;
        }

        public static List<string> ParseCidr(string cidr)
        {
            List<string> ips = new List<string>();

            try
            {
                if (string.IsNullOrWhiteSpace(cidr))
                {
                    return ips;
                }

                string[] parts = cidr.Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress ip) || !int.TryParse(parts[1], out int CIDR) || CIDR > 32 || CIDR < 0)
                {
                    return ips;
                }

                uint ipAddr = BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0);
                uint ipStart = ipAddr & ~((1u << (32 - CIDR)) - 1);
                uint ipEnd = ipStart + ((1u << (32 - CIDR)) - 1);

                for (uint ipNum = ipStart; ipNum <= ipEnd && ipNum >= ipStart; ipNum++)
                {
                    ips.Add(new IPAddress(BitConverter.GetBytes(ipNum).Reverse().ToArray()).ToString());
                }
            }
            catch (Exception ex)
            {

            }

            return ips;
        }

        public static List<string> GetDistinctSubnets(List<string> ipList)
        {
            HashSet<string> subnets = new HashSet<string>();
            foreach (var ip in ipList)
            {
                string subnet = GetSubnet(ip);
                subnets.Add(subnet);
            }
            return subnets.ToList();

            string GetSubnet(string ipAddress)
            {
                string[] parts = ipAddress.Split('.');
                return $"{parts[0]}.{parts[1]}.{parts[2]}.0/24";
            }
        }

        private static readonly SemaphoreSlim locker_asyncJsonToFile = new SemaphoreSlim(1);
        /// <summary>
        /// snmpQueue 資料寫入檔案
        /// </summary>
        /// <returns></returns>
        public static async Task asyncSnmpDataToFile()
        {
            if (!await locker_asyncJsonToFile.WaitAsync(1000).ConfigureAwait(false))
            {
                return;
            }

            string fileNameTop = "";
            int fileDataMaxCount = 100;

            string path = $"{Directory.GetCurrentDirectory()}\\CrawlerResponse\\SnmpData";

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                // 取出 Queue Data
                List<List<SnmpTaskInfo>> fileDataList = new List<List<SnmpTaskInfo>>();
                List<SnmpTaskInfo> tmpFileData = new List<SnmpTaskInfo>();

                int queueCount = ApiTool.snmpResQueue.Count;

                for (int num = 0; num < queueCount; num++)
                {
                    if (!ApiTool.snmpResQueue.TryDequeue(out SnmpTaskInfo data))
                    {
                        continue;
                    }

                    tmpFileData.Add(data);

                    if (tmpFileData.Count >= fileDataMaxCount)
                    {
                        fileDataList.Add(new List<SnmpTaskInfo>(tmpFileData));
                        tmpFileData.Clear();
                    }
                }

                if (tmpFileData.Count > 0)
                {
                    fileDataList.Add(new List<SnmpTaskInfo>(tmpFileData));
                    tmpFileData.Clear();
                }

                if (fileDataList.Count == 0)
                {
                    return;
                }

                foreach (var dataList in fileDataList)
                {
                    string json = JsonConvert.SerializeObject(dataList);
                    string fileName = DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".txt";
                    string filePath = Path.Combine(path, fileName);

                    //避免重複檔名
                    while (File.Exists(filePath))
                    {
                        await Task.Delay(1).ConfigureAwait(false);

                        fileName = DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".txt";
                        filePath = Path.Combine(path, fileName);
                    }

                    await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
                }


            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            finally
            {
                locker_asyncJsonToFile.Release();
            }

        }

    }
}
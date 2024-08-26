using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CollectorApi.Controllers.Collector.SnmpController;

namespace CollectorApi.Tool
{
    public class test
    {
        /// <summary>
        /// 產生Snmp任務清單
        /// </summary>
        /// <returns></returns>
        public static List<DeviceServiceModule> CreateSensorList()
        {
            List<DeviceServiceModule> resultList = new List<DeviceServiceModule>();

            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 10;
                mission.ServiceName = "CPU";
                mission.Oid = ".1.3.6.1.2.1.25.3.3.1.2";
                mission.OidDetail = 30;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.Version = 20;
                mission.ServiceMode = 121;
                mission.ServiceName = "HostName";
                mission.Oid = ".1.3.6.1.2.1.1.5.0";
                mission.OidDetail = 80;
                mission.DataType = 10;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.Version = 20;
                mission.ServiceMode = 122;
                mission.ServiceName = "SysDescr";
                mission.Oid = ".1.3.6.1.2.1.1.1.0";
                mission.OidDetail = 80;
                mission.DataType = 10;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.Version = 20;
                mission.ServiceMode = 80;
                mission.ServiceName = "NetInterface";
                mission.Oid = ".1.3.6.1.2.1.2.2.1.2";
                mission.OidDetail = 80;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.Version = 20;
                mission.ServiceMode = 85;
                mission.ServiceName = "StorageInterface";
                mission.Oid = ".1.3.6.1.2.1.25.2.3.1.3";
                mission.OidDetail = 80;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.Version = 20;
                mission.ServiceMode = 65;
                mission.ServiceName = "RegisterIp";
                mission.Oid = ".1.3.6.1.2.1.4.22.1.3";
                mission.OidDetail = 80;
                mission.DataType = 20;
                resultList.Add(mission);
            }
            {
                DeviceServiceModule mission = new DeviceServiceModule();
                mission.ServiceMode = 123;
                mission.ServiceName = "ipForwarding";
                mission.Oid = ".1.3.6.1.2.1.4.1.0";
                mission.OidDetail = 80;
                mission.DataType = 10;
                resultList.Add(mission);
            }

            return resultList;
        }

    }
}
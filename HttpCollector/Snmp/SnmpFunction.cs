
using System.Net;
using SnmpSharpNet;

namespace Collector.Snmp
{
    public class SnmpFunction
    {
        public async Task<List<string>> OidGet(SnmpParam snmpParam, DeviceServiceModule oidInfo)
        {
            var returnList = new List<string>();

            CreateSnmpParam(snmpParam, out AgentParameters snmpV2Param, out SecureAgentParameters snmpV3Param);

            Pdu pdu = new Pdu(PduType.Get);
            pdu.VbList.Add(oidInfo.Oid);

            string snmpRes = string.Empty;

            try
            {
                switch (snmpParam.Version)
                {
                    case eSnmpVersion.v1:
                        SnmpV1Packet snmpV1Result = (SnmpV1Packet)snmpParam.Target.Request(pdu, snmpV2Param);
                        foreach (var v in snmpV1Result.Pdu.VbList)
                        {
                            if (v.Value.ToString().ToLower().Contains("no-such-object"))
                            {
                                returnList = new List<string>();
                                throw new SnmpException("No-Such-Object");
                            }

                            returnList.Add(v.Value.ToString());
                        }
                        break;
                    case eSnmpVersion.v2c:
                        SnmpV2Packet snmpV2Result = (SnmpV2Packet)snmpParam.Target.Request(pdu, snmpV2Param);
                        foreach (var v in snmpV2Result.Pdu.VbList)
                        {
                            if (v.Value.ToString().ToLower().Contains("no-such-object"))
                            {
                                returnList = new List<string>();
                                throw new SnmpException("No-Such-Object");
                            }

                            returnList.Add(v.Value.ToString());
                        }
                        break;
                    case eSnmpVersion.v3:
                        SnmpV3Packet snmpV3Result = (SnmpV3Packet)snmpParam.Target.Request(pdu, snmpV3Param);
                        foreach (var v in snmpV3Result.ScopedPdu.VbList)
                        {
                            if (v.Value.ToString().ToLower().Contains("no-such-object"))
                            {
                                returnList = new List<string>();
                                throw new SnmpException("No-Such-Object");
                            }

                            returnList.Add(v.Value.ToString());
                        }
                        break;
                }
            }
            catch (SnmpException ex)
            {
                throw ex;
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                if (returnList.Count > 0)
                {
                    if (oidInfo.ServiceMode == 145)
                    {
                        //Parse SNMP hexadecimal time
                        snmpRes = Common.Tool.ParseSnmpTime(returnList[0]);
                    }
                    else
                    {
                        snmpRes = $"{string.Join(Environment.NewLine, returnList)}";
                    }
                }
            }

            return returnList;
        }

        public async Task<List<string>> OidGetBulk(SnmpParam snmpParam, DeviceServiceModule oidInfo)
        {
            var returnList = new List<string>();

            CreateSnmpParam(snmpParam, out AgentParameters snmpV2Param, out SecureAgentParameters snmpV3Param);

            //Bulk
            Pdu pdu = new Pdu(PduType.GetBulk);
            Oid rootOid = new Oid(oidInfo.Oid);
            Oid lastOid = (Oid)rootOid.Clone();

            pdu.NonRepeaters = 0;
            // MaxRepetitions tells the agent how many Oid/Value pairs to return
            // in the response.
            pdu.MaxRepetitions = 5;

            string snmpRes = string.Empty;

            while (lastOid != null)
            {
                if (pdu.RequestId != 0)
                {
                    pdu.RequestId += 1;
                }

                // Make SNMP request
                try
                {
                    // Clear Oids from the Pdu class.
                    pdu.VbList.Clear();
                    // Initialize request PDU with the last retrieved Oid
                    pdu.VbList.Add(lastOid);

                    switch (snmpParam.Version)
                    {
                        case eSnmpVersion.v1:
                            SnmpV1Packet snmpV1Result = (SnmpV1Packet)snmpParam.Target.Request(pdu, snmpV2Param);
                            foreach (var v in snmpV1Result.Pdu.VbList)
                            {
                                // Check that retrieved Oid is "child" of the root OID
                                if (rootOid.IsRootOf(v.Oid))
                                {
                                    returnList.Add(v.Value.ToString());
                                    if (v.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW)
                                        lastOid = null;
                                    else
                                        lastOid = v.Oid;
                                }
                                else
                                {
                                    // we have reached the end of the requested
                                    // MIB tree. Set lastOid to null and exit loop
                                    lastOid = null;
                                }
                            }
                            break;
                        case eSnmpVersion.v2c:
                            SnmpV2Packet snmpV2Result = (SnmpV2Packet)snmpParam.Target.Request(pdu, snmpV2Param);
                            foreach (var v in snmpV2Result.Pdu.VbList)
                            {
                                // Check that retrieved Oid is "child" of the root OID
                                if (rootOid.IsRootOf(v.Oid))
                                {
                                    returnList.Add(v.Value.ToString());
                                    if (v.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW)
                                        lastOid = null;
                                    else
                                        lastOid = v.Oid;
                                }
                                else
                                {
                                    // we have reached the end of the requested
                                    // MIB tree. Set lastOid to null and exit loop
                                    lastOid = null;
                                }
                            }
                            break;
                        case eSnmpVersion.v3:
                            SnmpV3Packet snmpV3Result = (SnmpV3Packet)snmpParam.Target.Request(pdu, snmpV3Param);
                            foreach (var v in snmpV3Result.ScopedPdu.VbList)
                            {
                                // Check that retrieved Oid is "child" of the root OID
                                if (rootOid.IsRootOf(v.Oid))
                                {
                                    returnList.Add(v.Value.ToString());
                                    if (v.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW)
                                        lastOid = null;
                                    else
                                        lastOid = v.Oid;
                                }
                                else
                                {
                                    // we have reached the end of the requested
                                    // MIB tree. Set lastOid to null and exit loop
                                    lastOid = null;
                                }
                            }
                            break;
                    }
                }
                catch (SnmpException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    if (oidInfo.ServiceMode == 145)
                    {
                        //Parse SNMP hexadecimal time
                        snmpRes = Common.Tool.ParseSnmpTime(returnList[0]);
                    }
                    else
                    {
                        snmpRes = $"{string.Join(Environment.NewLine, returnList)}";
                    }
                }

            }
            return returnList;
        }

        public async Task<List<OidReturnModule>> OidGetBulk_InterfaceOrStorage(SnmpParam snmpParam, DeviceServiceModule oidInfo)
        {
            var returnList = new List<OidReturnModule>();

            CreateSnmpParam(snmpParam, out AgentParameters snmpV2Param, out SecureAgentParameters snmpV3Param);

            //Bulk
            Pdu pdu = new Pdu(PduType.GetBulk);
            Oid rootOid = new Oid(oidInfo.Oid);
            Oid lastOid = (Oid)rootOid.Clone();

            pdu.NonRepeaters = 0;
            // MaxRepetitions tells the agent how many Oid/Value pairs to return
            // in the response.
            pdu.MaxRepetitions = 5;

            string snmpRes = string.Empty;

            while (lastOid != null)
            {
                if (pdu.RequestId != 0)
                {
                    pdu.RequestId += 1;
                }

                // Make SNMP request
                try
                {
                    // Clear Oids from the Pdu class.
                    pdu.VbList.Clear();
                    // Initialize request PDU with the last retrieved Oid
                    pdu.VbList.Add(lastOid);

                    switch (snmpParam.Version)
                    {
                        case eSnmpVersion.v1:
                            SnmpV1Packet snmpV1Result = (SnmpV1Packet)snmpParam.Target.Request(pdu, snmpV2Param);
                            foreach (var v in snmpV1Result.Pdu.VbList)
                            {
                                // Check that retrieved Oid is "child" of the root OID
                                if (rootOid.IsRootOf(v.Oid))
                                {
                                    OidReturnModule snmpInfo = new OidReturnModule
                                    {
                                        Oid = v.Oid.ToString()
                                    };

                                    if (int.TryParse(v.Value.ToString(), out int i))
                                    {
                                        snmpInfo.Value = Convert.ToInt64(v.Value.ToString());
                                    }
                                    else
                                    {
                                        snmpInfo.OidName = v.Value.ToString();
                                    }

                                    returnList.Add(snmpInfo);

                                    if (v.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW)
                                        lastOid = null;
                                    else
                                        lastOid = v.Oid;
                                }
                                else
                                {
                                    // we have reached the end of the requested
                                    // MIB tree. Set lastOid to null and exit loop
                                    lastOid = null;
                                }
                            }
                            break;
                        case eSnmpVersion.v2c:
                            SnmpV2Packet snmpV2Result = (SnmpV2Packet)snmpParam.Target.Request(pdu, snmpV2Param);
                            foreach (var v in snmpV2Result.Pdu.VbList)
                            {
                                // Check that retrieved Oid is "child" of the root OID
                                if (rootOid.IsRootOf(v.Oid))
                                {
                                    OidReturnModule snmpInfo = new OidReturnModule
                                    {
                                        Oid = v.Oid.ToString()
                                    };

                                    if (int.TryParse(v.Value.ToString(), out int i))
                                    {
                                        snmpInfo.Value = Convert.ToInt64(v.Value.ToString());
                                    }
                                    else
                                    {
                                        snmpInfo.OidName = v.Value.ToString();
                                    }

                                    returnList.Add(snmpInfo);

                                    if (v.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW)
                                        lastOid = null;
                                    else
                                        lastOid = v.Oid;
                                }
                                else
                                {
                                    // we have reached the end of the requested
                                    // MIB tree. Set lastOid to null and exit loop
                                    lastOid = null;
                                }
                            }
                            break;
                        case eSnmpVersion.v3:
                            SnmpV3Packet snmpV3Result = (SnmpV3Packet)snmpParam.Target.Request(pdu, snmpV3Param);
                            foreach (var v in snmpV3Result.ScopedPdu.VbList)
                            {
                                // Check that retrieved Oid is "child" of the root OID
                                if (rootOid.IsRootOf(v.Oid))
                                {
                                    OidReturnModule snmpInfo = new OidReturnModule
                                    {
                                        Oid = v.Oid.ToString()
                                    };

                                    if (int.TryParse(v.Value.ToString(), out int i))
                                    {
                                        snmpInfo.Value = Convert.ToInt64(v.Value.ToString());
                                    }
                                    else
                                    {
                                        snmpInfo.OidName = v.Value.ToString();
                                    }

                                    returnList.Add(snmpInfo);

                                    if (v.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW)
                                        lastOid = null;
                                    else
                                        lastOid = v.Oid;
                                }
                                else
                                {
                                    // we have reached the end of the requested
                                    // MIB tree. Set lastOid to null and exit loop
                                    lastOid = null;
                                }
                            }
                            break;
                    }
                }
                catch (SnmpException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return returnList;
        }

        /// <summary>
        /// SnmpSharpNet Param
        /// </summary>
        /// <returns></returns>
        private static void CreateSnmpParam(SnmpParam snmpParam, out AgentParameters snmpV2Param, out SecureAgentParameters snmpV3Param)
        {
            int timeoutMs = 2000;
            int retry = 1;

            IpAddress agent = new IpAddress(snmpParam.IP);
            snmpParam.Target = new UdpTarget((IPAddress)agent, snmpParam.Port, timeoutMs, retry);

            OctetString community = new OctetString(snmpParam.Community);
            snmpV2Param = new AgentParameters(community);
            snmpV3Param = new SecureAgentParameters();

            //Snmp V3 參數
            switch (snmpParam.Version)
            {
                case eSnmpVersion.v2c:
                    // Set SNMP version to 1 (or 2)
                    snmpV2Param.Version = SnmpVersion.Ver2;

                    //Snmp參數
                    snmpParam.Version = eSnmpVersion.v2c;
                    break;
                case eSnmpVersion.v3:
                    try
                    {
                        if (!snmpParam.Target.Discovery(snmpV3Param))
                        {
                            snmpParam.Target.Close();

                            throw new SnmpNetworkException($"IP#{snmpParam.IP} discovery failed. Unable to continue...");
                        }
                    }
                    catch (SnmpNetworkException ex)
                    {
                        // NctCollectorService.WriteTimeLog($"IP#{snmpParam.IP} destination network is unreachable.", NctCollectorService.elogLevel.WARN, stackTrace: ex.StackTrace);
                    }
                    catch (Exception ex)
                    {
                        // NctCollectorService.WriteTimeLog($"{ex.Message}", NctCollectorService.elogLevel.ERROR, stackTrace: ex.StackTrace);
                    }

                    int auth = 0;
                    int privacy = 0;

                    switch (snmpParam.AuthProtocol)
                    {
                        case eSnmpAuthProtocol.MD5:
                            auth = (int)AuthenticationDigests.MD5;
                            break;
                        case eSnmpAuthProtocol.SHA:
                            privacy = (int)AuthenticationDigests.SHA1;
                            break;
                        default:
                            privacy = (int)AuthenticationDigests.None;
                            break;
                    }
                    switch (snmpParam.PrivacyProtocol)
                    {
                        case eSnmpPrivacyProtocol.DES:
                            privacy = (int)PrivacyProtocols.DES;
                            break;
                        case eSnmpPrivacyProtocol.AES128:
                            privacy = (int)PrivacyProtocols.AES128;
                            break;
                        case eSnmpPrivacyProtocol.AES192:
                            privacy = (int)PrivacyProtocols.AES192;
                            break;
                        case eSnmpPrivacyProtocol.AES256:
                            privacy = (int)PrivacyProtocols.AES256;
                            break;
                        case eSnmpPrivacyProtocol.TripleDES:
                            privacy = (int)PrivacyProtocols.TripleDES;
                            break;
                        default:
                            privacy = (int)PrivacyProtocols.None;
                            break;
                    }

                    snmpV3Param.authPriv(snmpParam.SecurityName,
                       (AuthenticationDigests)auth, snmpParam.AuthPassword,
                       (PrivacyProtocols)privacy, snmpParam.PrivacyPassword);

                    break;
            }

        }
    }
}
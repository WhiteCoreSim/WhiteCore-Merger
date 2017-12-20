/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework.Servers.HttpServer;

namespace OpenSim.Framework.Communications.Services
{
    public class GridInfoService
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Hashtable _info = new Hashtable();

        /// <summary>
        /// Instantiate a GridInfoService object.
        /// </summary>
        /// <param name="configPath">path to config path containing
        /// grid information</param>
        /// <remarks>
        /// GridInfoService uses the [GridInfo] section of the
        /// standard OpenSim.ini file --- which is not optimal, but
        /// anything else requires a general redesign of the config
        /// system.
        /// </remarks>
        public GridInfoService(IConfigSource configSource)
        {
            loadGridInfo(configSource);
        }

        /// <summary>
        /// Default constructor, uses OpenSim.ini.
        /// </summary>
        public GridInfoService()
        {
            try
            {
                IConfigSource configSource = new IniConfigSource(Path.Combine(Util.configDir(), "OpenSim.ini"));
                loadGridInfo(configSource);
            }
            catch (FileNotFoundException)
            {
                _log.Warn("[GridInfoService] no OpenSim.ini file found --- GridInfoServices WILL NOT BE AVAILABLE to your users");
            }
        }

        private void loadGridInfo(IConfigSource configSource)
        {
            _info["platform"] = "OpenSim";
            try
            {
                IConfig startupCfg = configSource.Configs["Startup"];
                IConfig gridCfg = configSource.Configs["GridInfo"];
                IConfig netCfg = configSource.Configs["Network"];

                bool grid = startupCfg.GetBoolean("gridmode", false);

                if (grid)
                    _info["mode"] = "grid";
                else
                    _info["mode"] = "standalone";


                if (null != gridCfg)
                {
                    foreach (string k in gridCfg.GetKeys())
                    {
                        _info[k] = gridCfg.GetString(k);
                    }
                }
                else if (null != netCfg)
                {
                    if (grid)
                        _info["login"] 
                            = netCfg.GetString(
                                "user_server_url", "http://127.0.0.1:" + ConfigSettings.DefaultUserServerHttpPort.ToString());
                    else
                        _info["login"] 
                            = String.Format(
                                "http://127.0.0.1:{0}/", 
                                netCfg.GetString(
                                    "http_listener_port", ConfigSettings.DefaultRegionHttpPort.ToString()));
                    
                    IssueWarning();
                }
                else
                {
                    _info["login"] = "http://127.0.0.1:9000/";
                    IssueWarning();
                }
            }
            catch (Exception)
            {
                _log.Debug("[GridInfoService] cannot get grid info from config source, using minimal defaults");
            }
            _log.InfoFormat("[GridInfoService] Grid info service initialized with {0} keys", _info.Count);

        }

        private void IssueWarning()
        {
            _log.Warn("[GridInfoService] found no [GridInfo] section in your OpenSim.ini");
            _log.Warn("[GridInfoService] trying to guess sensible defaults, you might want to provide better ones:");
            foreach (string k in _info.Keys)
            {
                _log.WarnFormat("[GridInfoService] {0}: {1}", k, _info[k]);
            }
        }

        public XmlRpcResponse XmlRpcGridInfoMethod(XmlRpcRequest request)
        {
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();

            _log.Info("[GridInfo]: Request for grid info");

            foreach (string k in _info.Keys)
            {
                responseData[k] = _info[k];
            }
            response.Value = responseData;

            return response;
        }

        public string RestGetGridInfoMethod(string request, string path, string param,
                                            OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("<gridinfo>\n");
            foreach (string k in _info.Keys)
            {
                sb.AppendFormat("<{0}>{1}</{0}>\n", k, _info[k]);
            }
            sb.Append("</gridinfo>\n");

            return sb.ToString();
        }
    }
}

/*
 * Copyright (c) Contributors, http://www.openmetaverse.org/
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
using System.Reflection;
using log4net;
using OpenSim.Data;

namespace OpenSim.Framework.Communications.Cache
{
    public class SQLAssetServer : AssetServerBase
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region IPlugin

        public override string Name
        {
            get { return "SQL"; }
        }

        public override string Version
        {
            get { return "1.0"; }
        }

        public override void Initialize(ConfigSettings p_set)
        {
            m_log.Debug("[SQLAssetServer]: Plugin configured initialisation");
            Initialize(p_set.StandaloneAssetPlugin,p_set.StandaloneAssetSource);
        }

        #endregion

        public SQLAssetServer() {}

        public SQLAssetServer(string pluginName, string connect)
        {
            m_log.Debug("[SQLAssetServer]: Direct constructor");
            Initialize(pluginName, connect);
        }

        public void Initialize(string pluginName, string connect)
        {
            AddPlugin(pluginName, connect);
        }

        public SQLAssetServer(IAssetDataPlugin assetProvider)
        {
            m_assetProvider = assetProvider;
        }

        public void AddPlugin(string FileName, string connect)
        {
            m_log.Info("[SQLAssetServer]: AssetStorage: Attempting to load " + FileName);
            Assembly pluginAssembly = Assembly.LoadFrom(FileName);

            foreach (Type pluginType in pluginAssembly.GetTypes())
            {
                if (!pluginType.IsAbstract)
                {
                    Type typeInterface = pluginType.GetInterface("IAssetDataPlugin", true);

                    if (typeInterface != null)
                    {
                        IAssetDataPlugin plug =
                            (IAssetDataPlugin) Activator.CreateInstance(pluginAssembly.GetType(pluginType.ToString()));
                        m_assetProvider = plug;
                        m_assetProvider.Initialize(connect);

                        m_log.Info("[AssetStorage]: " +
                                   "Added " + m_assetProvider.Name + " " +
                                   m_assetProvider.Version);
                    }
                }
            }
        }

        protected override AssetBase GetAsset(AssetRequest req)
        {
            return m_assetProvider.FetchAsset(req.AssetID);
        }

        public override void StoreAsset(AssetBase asset)
        {
            m_assetProvider.CreateAsset(asset);
        }
    }
}

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

using log4net;
using Nini.Config;
using System;
using System.Collections.Generic;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Servers.Base;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.ServiceConnectors.Asset
{
    public class LocalAssetServicesConnector :
            ISharedRegionModule, IAssetService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private IImprovedAssetCache m_Cache = null;

        private IAssetService m_AssetService;

        private bool m_Enabled = false;

        public string Name
        {
            get { return "LocalAssetServicesConnector"; }
        }

        public void Initialize(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];
            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("AssetServices", "");
                if (name == Name)
                {
                    IConfig assetConfig = source.Configs["AssetService"];
                    if (assetConfig == null)
                    {
                        m_log.Error("[ASSET CONNECTOR]: AssetService missing from OpenSim.ini");
                        return;
                    }

                    string serviceDll = assetConfig.GetString("LocalServiceModule",
                            String.Empty);

                    if (serviceDll == String.Empty)
                    {
                        m_log.Error("[ASSET CONNECTOR]: No LocalServiceModule named in section AssetService");
                        return;
                    }

                    Object[] args = new Object[] { source };
                    m_AssetService =
                            ServerUtils.LoadPlugin<IAssetService>(serviceDll,
                            args);

                    if (m_AssetService == null)
                    {
                        m_log.Error("[ASSET CONNECTOR]: Can't load asset service");
                        return;
                    }
                    m_Enabled = true;
                    m_log.Info("[ASSET CONNECTOR]: Local asset connector enabled");
                }
            }
        }

        public void PostInitialize()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.RegisterModuleInterface<IAssetService>(this);
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_Cache == null)
            {
                m_Cache = scene.RequestModuleInterface<IImprovedAssetCache>();

                if (!(m_Cache is ISharedRegionModule))
                    m_Cache = null;
            }

            m_log.InfoFormat("[ASSET CONNECTOR]: Enabled local assets for region {0}", scene.RegionInfo.RegionName);

            if (m_Cache != null)
            {
                m_log.InfoFormat("[ASSET CONNECTOR]: Enabled asset caching for region {0}", scene.RegionInfo.RegionName);
            }
            else
            {
                // Short-circuit directly to storage layer
                //
                scene.UnregisterModuleInterface<IAssetService>(this);
                scene.RegisterModuleInterface<IAssetService>(m_AssetService);
            }
        }

        public AssetBase Get(string id)
        {
            AssetBase asset = m_Cache.Get(id);

            if (asset == null)
                return m_AssetService.Get(id);
            return asset;
        }

        public AssetMetadata GetMetadata(string id)
        {
            AssetBase asset = m_Cache.Get(id);

            if (asset != null)
                return asset.Metadata;

            asset = m_AssetService.Get(id);
            if (asset != null)
            {
                m_Cache.Cache(asset);
                return asset.Metadata;
            }

            return null;
        }

        public byte[] GetData(string id)
        {
            AssetBase asset = m_Cache.Get(id);

            if (asset != null)
                return asset.Data;

            asset = m_AssetService.Get(id);
            if (asset != null)
            {
                m_Cache.Cache(asset);
                return asset.Data;
            }

            return null;
        }

        public bool Get(string id, Object sender, AssetRetrieved handler)
        {
            AssetBase asset = m_Cache.Get(id);

            if (asset != null)
            {
                handler(id, sender, asset);
                return true;
            }

            return m_AssetService.Get(id, sender, delegate (string assetID, Object s, AssetBase a)
            {
                if (a != null)
                    m_Cache.Cache(a);
                handler(assetID, s, a);
            });
        }

        public string Store(AssetBase asset)
        {
            m_Cache.Cache(asset);
            if (asset.Temporary || asset.Local)
                return asset.ID;
            return m_AssetService.Store(asset);
        }

        public bool UpdateContent(string id, byte[] data)
        {
            AssetBase asset = m_Cache.Get(id);
            if (asset != null)
            {
                asset.Data = data;
                m_Cache.Cache(asset);
            }

            return m_AssetService.UpdateContent(id, data);
        }

        public bool Delete(string id)
        {
            m_Cache.Expire(id);

            return m_AssetService.Delete(id);
        }
    }
}

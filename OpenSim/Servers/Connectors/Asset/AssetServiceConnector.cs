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
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;

namespace OpenSim.Servers.Connectors
{
    public class AssetServicesConnector : IAssetService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;
        private IImprovedAssetCache m_Cache = null;

        public AssetServicesConnector()
        {
        }

        public AssetServicesConnector(IConfigSource source)
        {
            Initialise(source);
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig assetConfig = source.Configs["AssetService"];
            if (assetConfig == null)
            {
                m_log.Error("[ASSET CONNECTOR]: AssetService missing from OpanSim.ini");
                throw new Exception("Asset connector init error");
            }

            string serviceURI = assetConfig.GetString("AssetServerURI",
                    String.Empty);

            if (serviceURI == String.Empty)
            {
                m_log.Error("[ASSET CONNECTOR]: No Server URI named in section AssetService");
                throw new Exception("Asset connector init error");
            }
            m_ServerURI = serviceURI;
        }

        protected void SetCache(IImprovedAssetCache cache)
        {
            m_Cache = cache;
        }

        public AssetBase Get(string id)
        {
            string uri = m_ServerURI + "/assets/" + id;

            AssetBase asset = null;
            if (m_Cache != null)
                asset = m_Cache.Get(id);

            if (asset == null)
            {
                asset = SynchronousRestObjectRequester.
                        MakeRequest<int, AssetBase>("GET", uri, 0);

                if (m_Cache != null)
                    m_Cache.Cache(asset);
            }
            return asset;
        }

        public AssetMetadata GetMetadata(string id)
        {
            if (m_Cache != null)
            {
                AssetBase fullAsset = m_Cache.Get(id);

                if (fullAsset != null)
                    return fullAsset.Metadata;
            }

            string uri = m_ServerURI + "/assets/" + id + "/metadata";

            AssetMetadata asset = SynchronousRestObjectRequester.
                    MakeRequest<int, AssetMetadata>("GET", uri, 0);
            return asset;
        }

        public byte[] GetData(string id)
        {
            if (m_Cache != null)
            {
                AssetBase fullAsset = m_Cache.Get(id);

                if (fullAsset != null)
                    return fullAsset.Data;
            }

            RestClient rc = new RestClient(m_ServerURI);
            rc.AddResourcePath("assets");
            rc.AddResourcePath(id);
            rc.AddResourcePath("data");

            rc.RequestMethod = "GET";

            Stream s = rc.Request();

            if (s == null)
                return null;

            if (s.Length > 0)
            {
                byte[] ret = new byte[s.Length];
                s.Read(ret, 0, (int)s.Length);

                return ret;
            }

            return null;
        }

        public bool Get(string id, Object sender, AssetRetrieved handler)
        {
            string uri = m_ServerURI + "/assets/" + id;

            AssetBase asset = null;
            if (m_Cache != null)
                asset = m_Cache.Get(id);

            if (asset == null)
            {
                AsynchronousRestObjectRequester.
                        MakeRequest<int, AssetBase>("GET", uri, 0,
                        delegate(AssetBase a)
                        {
                            if (m_Cache != null)
                                m_Cache.Cache(a);
                            handler(id, sender, a);
                        });

            }
            else
            {
                handler(id, sender, asset);
            }

            return true;
        }

        public string Store(AssetBase asset)
        {
            if (asset.Temporary || asset.Local)
            {
                if (m_Cache != null)
                    m_Cache.Cache(asset);

                return asset.ID;
            }

            string uri = m_ServerURI + "/assets/";

            string newID = SynchronousRestObjectRequester.
                    MakeRequest<AssetBase, string>("POST", uri, asset);

            if (newID != String.Empty)
            {
                asset.ID = newID;

                if (m_Cache != null)
                    m_Cache.Cache(asset);
            }
            return newID;
        }

        public bool UpdateContent(string id, byte[] data)
        {
            AssetBase asset = null;

            if (m_Cache != null)
                asset = m_Cache.Get(id);

            if (asset == null)
            {
                AssetMetadata metadata = GetMetadata(id);
                if (metadata == null)
                    return false;

                asset = new AssetBase();
                asset.Metadata = metadata;
            }
            asset.Data = data;

            string uri = m_ServerURI + "/assets/" + id;

            if (SynchronousRestObjectRequester.
                    MakeRequest<AssetBase, bool>("POST", uri, asset))
            {
                if (m_Cache != null)
                    m_Cache.Cache(asset);

                return true;
            }
            return false;
        }

        public bool Delete(string id)
        {
            string uri = m_ServerURI + "/assets/" + id;

            if (SynchronousRestObjectRequester.
                    MakeRequest<int, bool>("DELETE", uri, 0))
            {
                if (m_Cache != null)
                    m_Cache.Expire(id);

                return true;
            }
            return false;
        }
    }
}

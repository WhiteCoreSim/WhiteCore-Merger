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

using OpenMetaverse;

namespace OpenSim.Framework
{
    /// <summary>
    /// Description of IAssetServer.
    /// </summary>
    public interface IAssetServer : IPlugin
    {
        void Initialize(ConfigSettings settings);
        void Initialize(ConfigSettings settings, string url, string dir, bool test);
        void Initialize(ConfigSettings settings, string url);
        
        /// <summary>
        /// Start the asset server
        /// </summary>
        void Start();
        
        /// <summary>
        /// Stop the asset server
        /// </summary>
        void Stop();
        
        void SetReceiver(IAssetReceiver receiver);
        void RequestAsset(UUID assetID, bool isTexture);
        void StoreAsset(AssetBase asset);
        void UpdateAsset(AssetBase asset);
    }

    /// <summary>
    /// Implemented by classes which with to asynchronously receive asset data from the asset service
    /// </summary>
    /// <remarks>could change to delegate?</remarks>
    public interface IAssetReceiver
    {
        /// <summary>
        /// Call back made when a requested asset has been retrieved by an asset server
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="IsTexture"></param>
        void AssetReceived(AssetBase asset, bool IsTexture);

        /// <summary>
        /// Call back made when an asset server could not retrieve a requested asset
        /// </summary>
        /// <param name="assetID"></param>
        /// <param name="IsTexture"></param>
        void AssetNotFound(UUID assetID, bool IsTexture);
    }

    public class AssetClientPluginInitializer : PluginInitializerBase
    {
        private ConfigSettings config;

        public AssetClientPluginInitializer (ConfigSettings p_sv)
        {
            config = p_sv;
        }
        public override void Initialize (IPlugin plugin)
        {
            IAssetServer p = plugin as IAssetServer;
            p.Initialize (config);
        }
    }

    public class LegacyAssetClientPluginInitializer : PluginInitializerBase
    {
        private ConfigSettings config;
        private string assetURL;

        public LegacyAssetClientPluginInitializer (ConfigSettings p_sv, string p_url)
        {
            config   = p_sv;
            assetURL = p_url;
        }
        public override void Initialize (IPlugin plugin)
        {
            IAssetServer p = plugin as IAssetServer;
            p.Initialize (config, assetURL);
        }
    }

    public class CryptoAssetClientPluginInitializer : PluginInitializerBase
    {
        private ConfigSettings config;
        private string assetURL;
        private string currdir;
        private bool   test;

        public CryptoAssetClientPluginInitializer (ConfigSettings p_sv, string p_url, string p_dir, bool p_test)
        {
            config   = p_sv;
            assetURL = p_url;
            currdir  = p_dir;
            test     = p_test;
        }
        public override void Initialize (IPlugin plugin)
        {
            IAssetServer p = plugin as IAssetServer;
            p.Initialize (config, assetURL, currdir, test);
        }
    }

    public interface IAssetPlugin
    {
        IAssetServer GetAssetServer();
    }

}

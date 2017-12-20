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
using Nini.Config;
using OpenSim.Servers.Base;
using OpenSim.Servers.AssetServer.Handlers;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;

namespace OpenSim.Servers.AssetServer
{
    public class AssetServiceConnector
    {
        private IAssetService m_AssetService;

        public AssetServiceConnector(IConfigSource config, IHttpServer server)
        {
            IConfig serverConfig = config.Configs["AssetService"];
            if (serverConfig == null)
                throw new Exception("No section 'Server' in config file");

            string assetService = serverConfig.GetString("LocalServiceModule",
                    String.Empty);

            if (assetService == String.Empty)
                throw new Exception("No AssetService in config file");

            Object[] args = new Object[] { config };
            m_AssetService =
                    ServerUtils.LoadPlugin<IAssetService>(assetService, args);

            server.AddStreamHandler(new AssetServerGetHandler(m_AssetService));
            server.AddStreamHandler(new AssetServerPostHandler(m_AssetService));
            server.AddStreamHandler(new AssetServerDeleteHandler(m_AssetService));
        }
    }
}

/*
 * Copyright (c) Contributors, http://whitecore-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 * For an explanation of the license of each contributor and the content it 
 * covers please see the Licenses directory.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the WhiteCore-Sim Project nor the
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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using log4net;
using Nini.Config;
using OpenMetaverse;

namespace OpenSim.Framework.AssetLoader.Filesystem
{
    /// <summary>
    ///     Loads assets from the filesystem location. Not yet a plugin though it should be.
    /// </summary>
    public class AssetLoaderFileSystem : IAssetLoader
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected static AssetBase CreateAsset(string assetIdStr, string name, string path, sbyte type)
        {
            AssetBase asset = new AssetBase(new UUID(assetIdStr), name, type);

            if (!String.IsNullOrEmpty(path))
            {
                LoadAsset(asset, path);
            }
            else
            {
                m_log.InfoFormat("[Assets]: Instantiated: [{0}]", name);
            }

            return asset;
        }

        protected static void LoadAsset(AssetBase info, string path)
        {
            FileInfo fInfo = new FileInfo(path);
            long numBytes = fInfo.Length;

            if (fInfo.Exists)
            {
                FileStream fStream = new FileStream(path, FileMode.Open, FileAccess.Read);
                byte[] idata = new byte[numBytes];
                BinaryReader br = new BinaryReader(fStream);
                idata = br.ReadBytes((int)numBytes);
                br.Close();
                fStream.Close();
                info.Data = idata;
            }
            else
            {
                m_log.ErrorFormat("[Assets]: file: [{0}] not found !", path);
            }
        }

        public void ForEachDefaultXmlAsset(string assetSetFilename, Action<AssetBase> action)
        {
            List<AssetBase> assets = new List<AssetBase>();

            if (File.Exists(assetSetFilename))
            {
                string assetSetPath = "ERROR";
                string assetRootPath = "";
                try
                {
                    XmlConfigSource source = new XmlConfigSource(assetSetFilename);
                    assetRootPath = Path.GetFullPath(source.SavePath);
                    assetRootPath = Path.GetDirectoryName(assetRootPath);

                    for (int i = 0; i < source.Configs.Count; i++)
                    {
                        assetSetPath = source.Configs[i].GetString("file", String.Empty);

                        LoadXmlAssetSet(Path.Combine(assetRootPath, assetSetPath), assets);
                    }
                }
                catch (XmlException e)
                {
                    m_log.ErrorFormat("[Assets]: Error loading {0} : {1}", assetSetPath, e);
                }
            }
            else
            {
                m_log.ErrorFormat("[Assets]: Asset set control file {0} does not exist!  No assets loaded.", assetSetFilename);
            }

            assets.ForEach(action);
        }

        /// <summary>
        ///     Use the asset set information at path to load assets
        /// </summary>
        /// <param name="assetSetPath"></param>
        /// <param name="assets"></param>
        protected static void LoadXmlAssetSet(string assetSetPath, List<AssetBase> assets)
        {
            if (File.Exists(assetSetPath))
            {
                try
                {
                    XmlConfigSource source = new XmlConfigSource(assetSetPath);
                    String dir = Path.GetDirectoryName(assetSetPath);

                    for (int i = 0; i < source.Configs.Count; i++)
                    {
                        string assetIdStr = source.Configs[i].GetString("assetID", UUID.Random().ToString());
                        string name = source.Configs[i].GetString("name", String.Empty);
                        sbyte type = (sbyte)source.Configs[i].GetInt("assetType", 0);
                        string assetPath = Path.Combine(dir, source.Configs[i].GetString("fileName", String.Empty));

                        AssetBase newAsset = CreateAsset(assetIdStr, name, assetPath, type);

                        newAsset.Type = type;
                        assets.Add(newAsset);
                    }
                }
                catch (XmlException e)
                {
                    m_log.ErrorFormat("[Assets]: Error loading {0} : {1}", assetSetPath, e);
                }
            }
            else
            {
                m_log.ErrorFormat("[Assets]: Asset set file {0} does not exist!", assetSetPath);
            }
        }
    }
}
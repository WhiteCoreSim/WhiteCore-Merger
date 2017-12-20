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
using System.IO;
using System.Text;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.World.Terrain.FileLoaders
{
    /// <summary>
    /// Terragen File Format Loader
    /// Built from specification at
    /// http://www.planetside.co.uk/terragen/dev/tgterrain.html
    /// </summary>
    internal class Terragen : ITerrainLoader
    {
        #region ITerrainLoader Members

        public ITerrainChannel LoadFile(string filename)
        {
            FileInfo file = new FileInfo(filename);
            FileStream s = file.Open(FileMode.Open, FileAccess.Read);
            ITerrainChannel retval = LoadStream(s);

            s.Close();

            return retval;
        }

        public ITerrainChannel LoadStream(Stream s)
        {
            TerrainChannel retval = new TerrainChannel();

            BinaryReader bs = new BinaryReader(s);

            bool eof = false;
            if (Encoding.ASCII.GetString(bs.ReadBytes(16)) == "TERRAGENTERRAIN ")
            {
                int w = 256;
                int h = 256;

                // Terragen file
                while (eof == false)
                {
                    string tmp = Encoding.ASCII.GetString(bs.ReadBytes(4));
                    switch (tmp)
                    {
                        case "SIZE":
                            int sztmp = bs.ReadInt16() + 1;
                            w = sztmp;
                            h = sztmp;
                            bs.ReadInt16();
                            break;
                        case "XPTS":
                            w = bs.ReadInt16();
                            bs.ReadInt16();
                            break;
                        case "YPTS":
                            h = bs.ReadInt16();
                            bs.ReadInt16();
                            break;
                        case "ALTW":
                            eof = true;
                            Int16 heightScale = bs.ReadInt16();
                            Int16 baseHeight = bs.ReadInt16();
                            retval = new TerrainChannel(w, h);
                            int x;
                            for (x = 0; x < w; x++)
                            {
                                int y;
                                for (y = 0; y < h; y++)
                                {
                                    retval[x, y] = baseHeight + bs.ReadInt16() * (double) heightScale / 65536.0;
                                }
                            }
                            break;
                        default:
                            bs.ReadInt32();
                            break;
                    }
                }
            }

            bs.Close();

            return retval;
        }

        public void SaveFile(string filename, ITerrainChannel map)
        {
            throw new NotImplementedException();
        }

        public void SaveStream(Stream stream, ITerrainChannel map)
        {
            throw new NotImplementedException();
        }

        public string FileExtension
        {
            get { return ".ter"; }
        }

        public ITerrainChannel LoadFile(string filename, int x, int y, int fileWidth, int fileHeight, int w, int h)
        {
            throw new NotImplementedException();
        }

        #endregion

        public override string ToString()
        {
            return "Terragen";
        }
    }
}

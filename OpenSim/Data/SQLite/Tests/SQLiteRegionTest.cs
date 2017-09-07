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
 *     * Neither the name of the OpenSimulator Project nor the
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

using System.IO;
using NUnit.Framework;
using OpenSim.Data.Tests;
using OpenSim.Tests.Common;

namespace OpenSim.Data.SQLite.Tests
{
    [TestFixture, DatabaseTest]
    public class SQLiteRegionTest : BasicRegionTest
    {
        public string file = "regiontest.db";
        public string connect;
        
        [TestFixtureSetUp]
        public void Init()
        {
            // SQLite doesn't work on power or z linux
            if (Directory.Exists("/proc/ppc64") || Directory.Exists("/proc/dasd"))
            {
                Assert.Ignore();
            }

            SuperInit();
            file = Path.GetTempFileName() + ".db";
            connect = "URI=file:" + file + ",version=3";
            db = new SQLiteRegionData();
            db.Initialize(connect);
        }

        [TestFixtureTearDown]
        public void Cleanup()
        {
            db.Dispose();
            File.Delete(file);
        }
    }
}

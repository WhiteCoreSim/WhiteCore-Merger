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

using System;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using log4net;
using OpenMetaverse;

namespace OpenSim.Server.Handlers.Asset
{
    public class XInventoryInConnector : ServiceConnector
    {
        private IInventoryService m_InventoryService;
        private string m_ConfigName = "InventoryService";

        public XInventoryInConnector(IConfigSource config, IHttpServer server, string configName) :
                base(config, server, configName)
        {
            if (configName != String.Empty)
                m_ConfigName = configName;

            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig == null)
                throw new Exception(String.Format("No section '{0}' in config file", m_ConfigName));

            string inventoryService = serverConfig.GetString("LocalServiceModule",
                    String.Empty);

            if (inventoryService == String.Empty)
                throw new Exception("No InventoryService in config file");

            Object[] args = new Object[] { config };
            m_InventoryService =
                    ServerUtils.LoadPlugin<IInventoryService>(inventoryService, args);

            server.AddStreamHandler(new XInventoryConnectorPostHandler(m_InventoryService));
        }
    }

    public class XInventoryConnectorPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IInventoryService m_InventoryService;

        public XInventoryConnectorPostHandler(IInventoryService service) :
                base("POST", "/xinventory")
        {
            m_InventoryService = service;
        }

        public override byte[] Handle(string path, Stream requestData,
                OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            StreamReader sr = new StreamReader(requestData);
            string body = sr.ReadToEnd();
            sr.Close();
            body = body.Trim();

            m_log.DebugFormat("[XXX]: query String: {0}", body);

            try
            {
                Dictionary<string, object> request =
                        ServerUtils.ParseQueryString(body);

                if (!request.ContainsKey("METHOD"))
                    return FailureResult();

                string method = request["METHOD"].ToString();
                request.Remove("METHOD");

                switch (method)
                {
                    case "CREATEUSERINVENTORY":
                        return HandleCreateUserInventory(request);
                    case "GETINVENTORYSKELETON":
                        return HandleGetInventorySkeleton(request);
                    case "GETROOTFOLDER":
                        return HandleGetRootFolder(request);
                    case "GETFOLDERFORTYPE":
                        return HandleGetFolderForType(request);
                    case "GETFOLDERCONTENT":
                        return HandleGetFolderContent(request);
                    case "GETFOLDERITEMS":
                        return HandleGetFolderItems(request);
                    case "ADDFOLDER":
                        return HandleAddFolder(request);
                    case "UPDATEFOLDER":
                        return HandleUpdateFolder(request);
                    case "MOVEFOLDER":
                        return HandleMoveFolder(request);
                    case "DELETEFOLDERS":
                        return HandleDeleteFolders(request);
                    case "PURGEFOLDER":
                        return HandlePurgeFolder(request);
                    case "ADDITEM":
                        return HandleAddItem(request);
                    case "UPDATEITEM":
                        return HandleUpdateItem(request);
                    case "MOVEITEMS":
                        return HandleMoveItems(request);
                    case "DELETEITEMS":
                        return HandleDeleteItems(request);
                    case "GETITEM":
                        return HandleGetItem(request);
                    case "GETFOLDER":
                        return HandleGetFolder(request);
                    case "GETACTIVEGESTURES":
                        return HandleGetActiveGestures(request);
                    case "GETASSETPERMISSIONS":
                        return HandleGetAssetPermissions(request);
                }
                m_log.DebugFormat("[XINVENTORY HANDLER]: unknown method request: {0}", method);
            }
            catch (Exception e)
            {
                m_log.Debug("[XINVENTORY HANDLER]: Exception {0}" + e);
            }

            return FailureResult();
        }

        private byte[] FailureResult()
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration,
                    "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse",
                    "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "RESULT", "");
            result.AppendChild(doc.CreateTextNode("False"));

            rootElement.AppendChild(result);

            return DocToBytes(doc);
        }

        private byte[] DocToBytes(XmlDocument doc)
        {
            MemoryStream ms = new MemoryStream();
            XmlTextWriter xw = new XmlTextWriter(ms, null);
            xw.Formatting = Formatting.Indented;
            doc.WriteTo(xw);
            xw.Flush();

            return ms.ToArray();
        }
        
        byte[] HandleCreateUserInventory(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            if (!request.ContainsKey("PRINCIPAL"))
                return FailureResult();

            if (m_InventoryService.CreateUserInventory(new UUID(request["PRINCIPAL"].ToString())))
                result["RESULT"] = "True";
            else
                result["RESULT"] = "False";

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetInventorySkeleton(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            if (!request.ContainsKey("PRINCIPAL"))
                return FailureResult();


            List<InventoryFolderBase> folders = m_InventoryService.GetInventorySkeleton(new UUID(request["PRINCIPAL"].ToString()));

            foreach (InventoryFolderBase f in folders)
                result[f.ID.ToString()] = EncodeFolder(f);

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetRootFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolderForType(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolderContent(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolderItems(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleAddFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleUpdateFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleMoveFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleDeleteFolders(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandlePurgeFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleAddItem(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleUpdateItem(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleMoveItems(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleDeleteItems(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetItem(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetFolder(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetActiveGestures(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        byte[] HandleGetAssetPermissions(Dictionary<string,object> request)
        {
            Dictionary<string,object> result = new Dictionary<string,object>();

            string xmlString = ServerUtils.BuildXmlResponse(result);
            m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(xmlString);
        }

        private Dictionary<string, object> EncodeFolder(InventoryFolderBase f)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();

            ret["ParentID"] = f.ParentID.ToString();
            ret["Type"] = f.Type.ToString();
            ret["Version"] = f.Version.ToString();
            ret["Name"] = f.Name;
            ret["Owner"] = f.Owner.ToString();
            ret["ID"] = f.ID.ToString();

            return ret;
        }

        private InventoryFolderBase BuildFolder(Dictionary<string,object> data)
        {
            InventoryFolderBase folder = new InventoryFolderBase();

            folder.ParentID =  new UUID(data["ParentID"].ToString());
            folder.Type = short.Parse(data["Type"].ToString());
            folder.Version = ushort.Parse(data["Version"].ToString());
            folder.Name = data["Name"].ToString();
            folder.Owner =  new UUID(data["Owner"].ToString());
            folder.ID = new UUID(data["ID"].ToString());

            return folder;
        }

        private InventoryItemBase BuildItem(Dictionary<string,object> data)
        {
            InventoryItemBase item = new InventoryItemBase();

            item.AssetID = new UUID(data["AssetID"].ToString());
            item.AssetType = int.Parse(data["AssetType"].ToString());
            item.Name = data["Name"].ToString();
            item.Owner = new UUID(data["Owner"].ToString());
            item.ID = new UUID(data["ID"].ToString());
            item.InvType = int.Parse(data["InvType"].ToString());
            item.Folder = new UUID(data["Folder"].ToString());
            item.CreatorId = data["CreatorId"].ToString();
            item.Description = data["Description"].ToString();
            item.NextPermissions = uint.Parse(data["NextPermissions"].ToString());
            item.CurrentPermissions = uint.Parse(data["CurrentPermissions"].ToString());
            item.BasePermissions = uint.Parse(data["BasePermissions"].ToString());
            item.EveryOnePermissions = uint.Parse(data["EveryOnePermissions"].ToString());
            item.GroupPermissions = uint.Parse(data["GroupPermissions"].ToString());
            item.GroupID = new UUID(data["GroupID"].ToString());
            item.GroupOwned = bool.Parse(data["GroupOwned"].ToString());
            item.SalePrice = int.Parse(data["SalePrice"].ToString());
            item.SaleType = byte.Parse(data["SaleType"].ToString());
            item.Flags = uint.Parse(data["Flags"].ToString());
            item.CreationDate = int.Parse(data["CreationDate"].ToString());

            return item;
        }
    }
}

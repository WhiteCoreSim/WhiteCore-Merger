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
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Timers;
using OpenMetaverse;
using OpenMetaverse.Packets;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes.Serialization;

namespace OpenSim.Region.Framework.Scenes
{
    public partial class Scene
    {
        private static readonly ILog m_log
            = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Allows asynchronous derezzing of objects from the scene into a client's inventory.
        /// </summary>
        protected AsyncSceneObjectGroupDeleter m_asyncSceneObjectDeleter;

        /// <summary>
        /// Start all the scripts in the scene which should be started.
        /// </summary>
        public void CreateScriptInstances()
        {
            m_log.Info("[PRIM INVENTORY]: Starting scripts in scene");

            foreach (EntityBase group in Entities)
            {
                if (group is SceneObjectGroup)
                {
                    ((SceneObjectGroup) group).CreateScriptInstances(0, false, DefaultScriptEngine, 0);
                }
            }
        }

        public void AddUploadedInventoryItem(UUID agentID, InventoryItemBase item)
        {
            IMoneyModule money=RequestModuleInterface<IMoneyModule>();
            if (money != null)
            {
                money.ApplyUploadCharge(agentID);
            }

            AddInventoryItem(agentID, item);
        }

        public bool AddInventoryItemReturned(UUID AgentId, InventoryItemBase item)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(AgentId);
            if (userInfo != null)
            {
                userInfo.AddItem(item);
                return true;
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Agent was not found for add of item {1} {2}", item.Name, item.ID);

                return false;
            }
        }

        public void AddInventoryItem(UUID AgentID, InventoryItemBase item)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(AgentID);

            if (userInfo != null)
            {
                userInfo.AddItem(item);

                int userlevel = 0;
                if (Permissions.IsGod(AgentID))
                {
                    userlevel = 1;
                }
                // TODO: remove this cruft once MasterAvatar is fully deprecated
                //
                if (m_regInfo.MasterAvatarAssignedUUID == AgentID)
                {
                    userlevel = 2;
                }
                EventManager.TriggerOnNewInventoryItemUploadComplete(AgentID, item.AssetID, item.Name, userlevel);
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Agent {1} was not found for add of item {2} {3}",
                    AgentID, item.Name, item.ID);

                return;
            }
        }

        /// <summary>
        /// Add an inventory item to an avatar's inventory.
        /// </summary>
        /// <param name="remoteClient">The remote client controlling the avatar</param>
        /// <param name="item">The item.  This structure contains all the item metadata, including the folder
        /// in which the item is to be placed.</param>
        public void AddInventoryItem(IClientAPI remoteClient, InventoryItemBase item)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            if (userInfo != null)
            {
                AddInventoryItem(remoteClient.AgentId, item);
                remoteClient.SendInventoryItemCreateUpdate(item, 0);
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Could not resolve user {0} for adding an inventory item",
                    remoteClient.AgentId);
            }
        }

        /// <summary>
        /// Capability originating call to update the asset of an item in an agent's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public virtual UUID CapsUpdateInventoryItemAsset(IClientAPI remoteClient, UUID itemID, byte[] data)
        {
            CachedUserInfo userInfo = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);
            if (userInfo != null)
            {
                if (userInfo.RootFolder != null)
                {
                    InventoryItemBase item = userInfo.RootFolder.FindItem(itemID);

                    if (item != null)
                    {
                        if ((InventoryType)item.InvType == InventoryType.Notecard)
                        {
                            if (!Permissions.CanEditNotecard(itemID, UUID.Zero, remoteClient.AgentId))
                            {
                                remoteClient.SendAgentAlertMessage("Insufficient permissions to edit notecard", false);
                                return UUID.Zero;
                            }

                            remoteClient.SendAgentAlertMessage("Notecard saved", false);
                        }
                        else if ((InventoryType)item.InvType == InventoryType.LSL)
                        {
                            if (!Permissions.CanEditScript(itemID, UUID.Zero, remoteClient.AgentId))
                            {
                                remoteClient.SendAgentAlertMessage("Insufficient permissions to edit script", false);
                                return UUID.Zero;
                            }

                            remoteClient.SendAgentAlertMessage("Script saved", false);
                        }

                        AssetBase asset =
                            CreateAsset(item.Name, item.Description, (sbyte)item.AssetType, data);
                        CommsManager.AssetCache.AddAsset(asset);

                        item.AssetID = asset.FullID;
                        userInfo.UpdateItem(item);

                        // remoteClient.SendInventoryItemCreateUpdate(item);
                        return (asset.FullID);
                    }
                }
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Could not resolve user {0} for caps inventory update",
                    remoteClient.AgentId);
            }

            return UUID.Zero;
        }

        /// <summary>
        /// <see>CapsUpdatedInventoryItemAsset(IClientAPI, UUID, byte[])</see>
        /// </summary>
        public UUID CapsUpdateInventoryItemAsset(UUID avatarId, UUID itemID, byte[] data)
        {
            ScenePresence avatar;

            if (TryGetAvatar(avatarId, out avatar))
            {
                return CapsUpdateInventoryItemAsset(avatar.ControllingClient, itemID, data);
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: " +
                    "Avatar {0} cannot be found to update its inventory item asset",
                    avatarId);
            }

            return UUID.Zero;
        }

        /// <summary>
        /// Capability originating call to update the asset of a script in a prim's (task's) inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="primID">The prim which contains the item to update</param>
        /// <param name="isScriptRunning">Indicates whether the script to update is currently running</param>
        /// <param name="data"></param>
        public void CapsUpdateTaskInventoryScriptAsset(IClientAPI remoteClient, UUID itemId,
                                                       UUID primId, bool isScriptRunning, byte[] data)
        {
            if (!Permissions.CanEditScript(itemId, primId, remoteClient.AgentId))
            {
                remoteClient.SendAgentAlertMessage("Insufficient permissions to edit script", false);
                return;
            }

            // Retrieve group
            SceneObjectPart part = GetSceneObjectPart(primId);
            SceneObjectGroup group = part.ParentGroup;
            if (null == group)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Prim inventory update requested for item ID {0} in prim ID {1} but this prim does not exist",
                    itemId, primId);

                return;
            }

            // Retrieve item
            TaskInventoryItem item = group.GetInventoryItem(part.LocalId, itemId);

            if (null == item)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for caps script update "
                        + " but the item does not exist in this inventory",
                    itemId, part.Name, part.UUID);

                return;
            }

            AssetBase asset = CreateAsset(item.Name, item.Description, (sbyte)AssetType.LSLText, data);
            CommsManager.AssetCache.AddAsset(asset);

            if (isScriptRunning)
            {
                part.Inventory.RemoveScriptInstance(item.ItemID);
            }

            // Update item with new asset
            item.AssetID = asset.FullID;
            group.UpdateInventoryItem(item);
            part.GetProperties(remoteClient);

            // Trigger rerunning of script (use TriggerRezScript event, see RezScript)
            if (isScriptRunning)
            {
                // Needs to determine which engine was running it and use that
                //
                part.Inventory.CreateScriptInstance(item.ItemID, 0, false, DefaultScriptEngine, 0);
            }
            else
            {
                remoteClient.SendAgentAlertMessage("Script saved", false);
            }
        }

        /// <summary>
        /// <see>CapsUpdateTaskInventoryScriptAsset(IClientAPI, UUID, UUID, bool, byte[])</see>
        /// </summary>
        public void CapsUpdateTaskInventoryScriptAsset(UUID avatarId, UUID itemId,
                                                        UUID primId, bool isScriptRunning, byte[] data)
        {
            ScenePresence avatar;

            if (TryGetAvatar(avatarId, out avatar))
            {
                CapsUpdateTaskInventoryScriptAsset(
                    avatar.ControllingClient, itemId, primId, isScriptRunning, data);
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Avatar {0} cannot be found to update its prim item asset",
                    avatarId);
            }
        }

        /// <summary>
        /// Update an item which is either already in the client's inventory or is within
        /// a transaction
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="transactionID">The transaction ID.  If this is UUID.Zero we will
        /// assume that we are not in a transaction</param>
        /// <param name="itemID">The ID of the updated item</param>
        /// <param name="name">The name of the updated item</param>
        /// <param name="description">The description of the updated item</param>
        /// <param name="nextOwnerMask">The permissions of the updated item</param>
/*        public void UpdateInventoryItemAsset(IClientAPI remoteClient, UUID transactionID,
                                             UUID itemID, string name, string description,
                                             uint nextOwnerMask)*/
        public void UpdateInventoryItemAsset(IClientAPI remoteClient, UUID transactionID,
                                             UUID itemID, InventoryItemBase itemUpd)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            if (userInfo != null && userInfo.RootFolder != null)
            {
                InventoryItemBase item = userInfo.RootFolder.FindItem(itemID);

                if (item != null)
                {
                    if (UUID.Zero == transactionID)
                    {
                        item.Name = itemUpd.Name;
                        item.Description = itemUpd.Description;
                        item.NextPermissions = itemUpd.NextPermissions;
                        item.CurrentPermissions |= 8; // Slam!
                        item.EveryOnePermissions = itemUpd.EveryOnePermissions;
                        item.GroupPermissions = itemUpd.GroupPermissions;

                        item.GroupID = itemUpd.GroupID;
                        item.GroupOwned = itemUpd.GroupOwned;
                        item.CreationDate = itemUpd.CreationDate;
                        // The client sends zero if its newly created?

                        if (itemUpd.CreationDate == 0)
                            item.CreationDate = Util.UnixTimeSinceEpoch();
                        else
                            item.CreationDate = itemUpd.CreationDate;

                        // TODO: Check if folder changed and move item
                        //item.NextPermissions = itemUpd.Folder;
                        item.InvType = itemUpd.InvType;
                        item.SalePrice = itemUpd.SalePrice;
                        item.SaleType = itemUpd.SaleType;
                        item.Flags = itemUpd.Flags;

                        userInfo.UpdateItem(item);
                    }
                    else
                    {
                        IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
                        if (agentTransactions != null)
                        {
                            agentTransactions.HandleItemUpdateFromTransaction(
                                         remoteClient, transactionID, item);
                        }
                    }
                }
                else
                {
                    m_log.Error(
                        "[AGENTINVENTORY]: Item ID " + itemID + " not found for an inventory item update.");
                }
            }
            else
            {
                m_log.Error(
                    "[AGENT INVENTORY]: Agent ID " + remoteClient.AgentId + " not found for an inventory item update.");
            }
        }

        /// <summary>
        /// Give an inventory item from one user to another
        /// </summary>
        /// <param name="recipientClient"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="itemId"></param>
        public virtual void GiveInventoryItem(IClientAPI recipientClient, UUID senderId, UUID itemId)
        {
            InventoryItemBase itemCopy = GiveInventoryItem(recipientClient.AgentId, senderId, itemId);

            if (itemCopy != null)
                recipientClient.SendBulkUpdateInventory(itemCopy);
        }

        /// <summary>
        /// Give an inventory item from one user to another
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="itemId"></param>
        /// <returns>The inventory item copy given, null if the give was unsuccessful</returns>
        public virtual InventoryItemBase GiveInventoryItem(UUID recipient, UUID senderId, UUID itemId)
        {
            return GiveInventoryItem(recipient, senderId, itemId, UUID.Zero);
        }

        /// <summary>
        /// Give an inventory item from one user to another
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="itemId"></param>
        /// <param name="recipientFolderId">
        /// The id of the folder in which the copy item should go.  If UUID.Zero then the item is placed in the most
        /// appropriate default folder.
        /// </param>
        /// <returns>
        /// The inventory item copy given, null if the give was unsuccessful
        /// </returns>
        public virtual InventoryItemBase GiveInventoryItem(
            UUID recipient, UUID senderId, UUID itemId, UUID recipientFolderId)
        {
            // Retrieve the item from the sender
            CachedUserInfo senderUserInfo = CommsManager.UserProfileCacheService.GetUserDetails(senderId);

            if (senderUserInfo == null)
            {
                m_log.ErrorFormat(
                     "[AGENT INVENTORY]: Failed to find sending user {0} for item {1}", senderId, itemId);

                return null;
            }

            if (senderUserInfo.RootFolder != null)
            {
                InventoryItemBase item = senderUserInfo.RootFolder.FindItem(itemId);

                if (item != null)
                {
                    if (!Permissions.BypassPermissions())
                    {
                        if ((item.CurrentPermissions & (uint)PermissionMask.Transfer) == 0)
                            return null;
                    }

                    CachedUserInfo recipientUserInfo
                        = CommsManager.UserProfileCacheService.GetUserDetails(recipient);

                    if (recipientUserInfo != null)
                    {
                        if (!recipientUserInfo.HasReceivedInventory)
                            recipientUserInfo.FetchInventory();

                        // Insert a copy of the item into the recipient
                        InventoryItemBase itemCopy = new InventoryItemBase();
                        itemCopy.Owner = recipient;
                        itemCopy.CreatorId = item.CreatorId;
                        itemCopy.ID = UUID.Random();
                        itemCopy.AssetID = item.AssetID;
                        itemCopy.Description = item.Description;
                        itemCopy.Name = item.Name;
                        itemCopy.AssetType = item.AssetType;
                        itemCopy.InvType = item.InvType;
                        itemCopy.Folder = recipientFolderId;

                        if (Permissions.PropagatePermissions())
                        {
                            if (item.InvType == (int)InventoryType.Object)
                            {
                                itemCopy.BasePermissions &= ~(uint)(PermissionMask.Copy | PermissionMask.Modify | PermissionMask.Transfer);
                                itemCopy.BasePermissions |= (item.CurrentPermissions & 7) << 13;
                            }
                            else
                            {
                                itemCopy.BasePermissions = item.BasePermissions & item.NextPermissions;
                            }

                            itemCopy.CurrentPermissions = itemCopy.BasePermissions;
                            if ((item.CurrentPermissions & 8) != 0) // Propagate slam bit
                            {
                                itemCopy.BasePermissions &= item.NextPermissions;
                                itemCopy.CurrentPermissions = itemCopy.BasePermissions;
                                itemCopy.CurrentPermissions |= 8;
                            }

                            itemCopy.NextPermissions = item.NextPermissions;
                            itemCopy.EveryOnePermissions = item.EveryOnePermissions & item.NextPermissions;
                            itemCopy.GroupPermissions = item.GroupPermissions & item.NextPermissions;
                        }
                        else
                        {
                            itemCopy.CurrentPermissions = item.CurrentPermissions;
                            itemCopy.NextPermissions = item.NextPermissions;
                            itemCopy.EveryOnePermissions = item.EveryOnePermissions & item.NextPermissions;
                            itemCopy.GroupPermissions = item.GroupPermissions & item.NextPermissions;
                            itemCopy.BasePermissions = item.BasePermissions;
                        }
                        
                        itemCopy.GroupID = UUID.Zero;
                        itemCopy.GroupOwned = false;
                        itemCopy.Flags = item.Flags;
                        itemCopy.SalePrice = item.SalePrice;
                        itemCopy.SaleType = item.SaleType;

                        recipientUserInfo.AddItem(itemCopy);

                        if (!Permissions.BypassPermissions())
                        {
                            if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                                senderUserInfo.DeleteItem(itemId);
                        }

                        return itemCopy;
                    }
                    else
                    {
                        m_log.ErrorFormat(
                            "[AGENT INVENTORY]: Could not find userinfo for recipient user {0} of item {1}, {2} from {3}",
                            recipient, item.Name,
                            item.ID, senderId);
                    }
                }
                else
                {
                    m_log.ErrorFormat(
                        "[AGENT INVENTORY]: Failed to find item {0} to give to {1}", itemId, senderId);

                    return null;
                }
            }
            else
            {
                m_log.Error("[AGENT INVENTORY]: Failed to find item " + itemId.ToString() + ", no root folder");
                return null;
            }

            return null;
        }

        /// <summary>
        /// Give an entire inventory folder from one user to another.  The entire contents (including all descendent
        /// folders) is given.
        /// </summary>
        /// <param name="recipientId"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="folderId"></param>
        /// <param name="recipientParentFolderId">
        /// The id of the receipient folder in which the send folder should be placed.  If UUID.Zero then the
        /// recipient folder is the root folder
        /// </param>
        /// <returns>
        /// The inventory folder copy given, null if the copy was unsuccessful
        /// </returns>
        public virtual InventoryFolderImpl GiveInventoryFolder(
            UUID recipientId, UUID senderId, UUID folderId, UUID recipientParentFolderId)
        {
            // Retrieve the folder from the sender
            CachedUserInfo senderUserInfo = CommsManager.UserProfileCacheService.GetUserDetails(senderId);

            if (null == senderUserInfo)
            {
                m_log.ErrorFormat(
                     "[AGENT INVENTORY]: Failed to find sending user {0} for folder {1}", senderId, folderId);

                return null;
            }

            if (!senderUserInfo.HasReceivedInventory)
            {
                m_log.DebugFormat(
                     "[AGENT INVENTORY]: Could not give inventory folder - have not yet received inventory for {0}",
                     senderId);

                return null;
            }

            InventoryFolderImpl folder = senderUserInfo.RootFolder.FindFolder(folderId);

            if (null == folder)
            {
                m_log.ErrorFormat(
                     "[AGENT INVENTORY]: Could not find inventory folder {0} to give", folderId);

                return null;
            }

            CachedUserInfo recipientUserInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(recipientId);

            if (null == recipientUserInfo)
            {
                m_log.ErrorFormat(
                     "[AGENT INVENTORY]: Failed to find receiving user {0} for folder {1}", recipientId, folderId);

                return null;
            }

            if (!recipientUserInfo.HasReceivedInventory)
            {
                recipientUserInfo.FetchInventory();
                if (!WaitForInventory(recipientUserInfo))
                    return null;
            }

            if (recipientParentFolderId == UUID.Zero)
                recipientParentFolderId = recipientUserInfo.RootFolder.ID;

            UUID newFolderId = UUID.Random();
            recipientUserInfo.CreateFolder(folder.Name, newFolderId, (ushort)folder.Type, recipientParentFolderId);

            // XXX: Messy - we should really get this back in the CreateFolder call
            InventoryFolderImpl copiedFolder = recipientUserInfo.RootFolder.FindFolder(newFolderId);

            // Give all the subfolders
            List<InventoryFolderImpl> subFolders = folder.RequestListOfFolderImpls();
            foreach (InventoryFolderImpl childFolder in subFolders)
            {
                GiveInventoryFolder(recipientId, senderId, childFolder.ID, copiedFolder.ID);
            }

            // Give all the items
            List<InventoryItemBase> items = folder.RequestListOfItems();
            foreach (InventoryItemBase item in items)
            {
                GiveInventoryItem(recipientId, senderId, item.ID, copiedFolder.ID);
            }

            return copiedFolder;
        }

        public void CopyInventoryItem(IClientAPI remoteClient, uint callbackID, UUID oldAgentID, UUID oldItemID,
                                      UUID newFolderID, string newName)
        {
            m_log.DebugFormat(
                "[AGENT INVENTORY]: CopyInventoryItem received by {0} with oldAgentID {1}, oldItemID {2}, new FolderID {3}, newName {4}",
                remoteClient.AgentId, oldAgentID, oldItemID, newFolderID, newName);

            InventoryItemBase item = CommsManager.UserProfileCacheService.LibraryRoot.FindItem(oldItemID);

            if (item == null)
            {
                CachedUserInfo userInfo = CommsManager.UserProfileCacheService.GetUserDetails(oldAgentID);
                if (userInfo == null)
                {
                    m_log.Error("[AGENT INVENTORY]: Failed to find user " + oldAgentID.ToString());
                    return;
                }

                if (userInfo.RootFolder != null)
                {
                    item = userInfo.RootFolder.FindItem(oldItemID);

                    if (item == null)
                    {
                        m_log.Error("[AGENT INVENTORY]: Failed to find item " + oldItemID.ToString());
                        return;
                    }
                }
                else
                {
                    m_log.Error("[AGENT INVENTORY]: Failed to find item " + oldItemID.ToString());
                    return;
                }
            }

            AssetBase asset
                = CommsManager.AssetCache.GetAsset(
                    item.AssetID, (item.AssetType == (int)AssetType.Texture ? true : false));

            if (asset != null)
            {
                if (newName != String.Empty)
                {
                    asset.Name = newName;
                }
                else
                {
                    newName = item.Name;
                }

                if (remoteClient.AgentId == oldAgentID)
                {
                    CreateNewInventoryItem(
                        remoteClient, newFolderID, newName, item.Flags, callbackID, asset, (sbyte)item.InvType,
                        item.BasePermissions, item.CurrentPermissions, item.EveryOnePermissions, item.NextPermissions, item.GroupPermissions, Util.UnixTimeSinceEpoch());
                }
                else
                {
                    CreateNewInventoryItem(
                        remoteClient, newFolderID, newName, item.Flags, callbackID, asset, (sbyte)item.InvType,
                        item.NextPermissions, item.NextPermissions, item.EveryOnePermissions & item.NextPermissions, item.NextPermissions, item.GroupPermissions, Util.UnixTimeSinceEpoch());
                }
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Could not copy item {0} since asset {1} could not be found",
                    item.Name, item.AssetID);
            }
        }

        /// <summary>
        /// Create a new asset data structure.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="description"></param>
        /// <param name="invType"></param>
        /// <param name="assetType"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        private AssetBase CreateAsset(string name, string description, sbyte assetType, byte[] data)
        {
            AssetBase asset = new AssetBase();
            asset.Name = name;
            asset.Description = description;
            asset.Type = assetType;
            asset.FullID = UUID.Random();
            asset.Data = (data == null) ? new byte[1] : data;

            return asset;
        }

        /// <summary>
        /// Move an item within the agent's inventory.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="itemID"></param>
        /// <param name="length"></param>
        /// <param name="newName"></param>
        public void MoveInventoryItem(IClientAPI remoteClient, UUID folderID, UUID itemID, int length,
                                      string newName)
        {
            m_log.DebugFormat(
                "[AGENT INVENTORY]: Moving item {0} to {1} for {2}", itemID, folderID, remoteClient.AgentId);

            CachedUserInfo userInfo = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            if (userInfo == null)
            {
                m_log.Error("[AGENT INVENTORY]: Failed to find user " + remoteClient.AgentId.ToString());

                return;
            }

            if (userInfo.RootFolder != null)
            {
                InventoryItemBase item = userInfo.RootFolder.FindItem(itemID);

                if (item != null)
                {
                    if (newName != String.Empty)
                    {
                        item.Name = newName;
                    }
                    item.Folder = folderID;

                    userInfo.DeleteItem(item.ID);

                    AddInventoryItem(remoteClient, item);
                }
                else
                {
                    m_log.Error("[AGENT INVENTORY]: Failed to find item " + itemID.ToString());

                    return;
                }
            }
            else
            {
                m_log.Error("[AGENT INVENTORY]: Failed to find item " + itemID.ToString() + ", no root folder");

                return;
            }
        }

        /// <summary>
        /// Create a new inventory item.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="callbackID"></param>
        /// <param name="asset"></param>
        /// <param name="invType"></param>
        /// <param name="nextOwnerMask"></param>
        private void CreateNewInventoryItem(IClientAPI remoteClient, UUID folderID, string name, uint flags, uint callbackID,
                                            AssetBase asset, sbyte invType, uint nextOwnerMask, int creationDate)
        {
            CreateNewInventoryItem(
                remoteClient, folderID, name, flags, callbackID, asset, invType,
                (uint)PermissionMask.All, (uint)PermissionMask.All, 0, nextOwnerMask, 0, creationDate);
        }

        /// <summary>
        /// Create a new Inventory Item
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="callbackID"></param>
        /// <param name="asset"></param>
        /// <param name="invType"></param>
        /// <param name="nextOwnerMask"></param>
        /// <param name="creationDate"></param>
        private void CreateNewInventoryItem(
            IClientAPI remoteClient, UUID folderID, string name, uint flags, uint callbackID, AssetBase asset, sbyte invType,
            uint baseMask, uint currentMask, uint everyoneMask, uint nextOwnerMask, uint groupMask, int creationDate)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            if (userInfo != null)
            {
                InventoryItemBase item = new InventoryItemBase();
                item.Owner = remoteClient.AgentId;
                item.CreatorId = remoteClient.AgentId.ToString();
                item.ID = UUID.Random();
                item.AssetID = asset.FullID;
                item.Description = asset.Description;
                item.Name = name;
                item.Flags = flags;
                item.AssetType = asset.Type;
                item.InvType = invType;
                item.Folder = folderID;
                item.CurrentPermissions = currentMask;
                item.NextPermissions = nextOwnerMask;
                item.EveryOnePermissions = everyoneMask;
                item.GroupPermissions = groupMask;
                item.BasePermissions = baseMask;
                item.CreationDate = creationDate;

                userInfo.AddItem(item);
                remoteClient.SendInventoryItemCreateUpdate(item, callbackID);
            }
            else
            {
                m_log.WarnFormat(
                    "No user details associated with client {0} uuid {1} in CreateNewInventoryItem!",
                     remoteClient.Name, remoteClient.AgentId);
            }
        }

        /// <summary>
        /// Create a new inventory item.  Called when the client creates a new item directly within their
        /// inventory (e.g. by selecting a context inventory menu option).
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="transactionID"></param>
        /// <param name="folderID"></param>
        /// <param name="callbackID"></param>
        /// <param name="description"></param>
        /// <param name="name"></param>
        /// <param name="invType"></param>
        /// <param name="type"></param>
        /// <param name="wearableType"></param>
        /// <param name="nextOwnerMask"></param>
        public void CreateNewInventoryItem(IClientAPI remoteClient, UUID transactionID, UUID folderID,
                                           uint callbackID, string description, string name, sbyte invType,
                                           sbyte assetType,
                                           byte wearableType, uint nextOwnerMask, int creationDate)
        {
            m_log.DebugFormat("[AGENT INVENTORY]: Received request to create inventory item {0} in folder {1}", name, folderID);

            if (!Permissions.CanCreateUserInventory(invType, remoteClient.AgentId))
                return;

            if (transactionID == UUID.Zero)
            {
                CachedUserInfo userInfo
                    = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

                if (userInfo != null)
                {
                    ScenePresence presence;
                    TryGetAvatar(remoteClient.AgentId, out presence);
                    byte[] data = null;

                    if (invType == (sbyte)InventoryType.Landmark && presence != null)
                    {
                        Vector3 pos = presence.AbsolutePosition;
                        string strdata = String.Format(
                            "Landmark version 2\nregion_id {0}\nlocal_pos {1} {2} {3}\nregion_handle {4}\n",
                            presence.Scene.RegionInfo.RegionID,
                            pos.X, pos.Y, pos.Z,
                            presence.RegionHandle);
                        data = Encoding.ASCII.GetBytes(strdata);
                    }

                    AssetBase asset = CreateAsset(name, description, assetType, data);
                    CommsManager.AssetCache.AddAsset(asset);

                    CreateNewInventoryItem(remoteClient, folderID, asset.Name, 0, callbackID, asset, invType, nextOwnerMask, creationDate);
                }
                else
                {
                    m_log.ErrorFormat(
                        "userInfo for agent uuid {0} unexpectedly null in CreateNewInventoryItem",
                        remoteClient.AgentId);
                }
            }
            else
            {
                IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
                if (agentTransactions != null)
                {
                    agentTransactions.HandleItemCreationFromTransaction(
                        remoteClient, transactionID, folderID, callbackID, description,
                        name, invType, assetType, wearableType, nextOwnerMask);
                }
            }
        }

        /// <summary>
        /// Remove an inventory item for the client's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        private void RemoveInventoryItem(IClientAPI remoteClient, UUID itemID)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            if (userInfo == null)
            {
                m_log.WarnFormat(
                    "[AGENT INVENTORY]: Failed to find user {0} {1} to delete inventory item {2}",
                    remoteClient.Name, remoteClient.AgentId, itemID);

                return;
            }

            userInfo.DeleteItem(itemID);
        }

        /// <summary>
        /// Removes an inventory folder.  Although there is a packet in the Linden protocol for this, it may be
        /// legacy and not currently used (purge folder is used to remove folders from trash instead).
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        private void RemoveInventoryFolder(IClientAPI remoteClient, UUID folderID)
        {
            CachedUserInfo userInfo
                = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

            if (userInfo == null)
            {
                m_log.Warn("[AGENT INVENTORY]: Failed to find user " + remoteClient.AgentId.ToString());
                return;
            }

            if (userInfo.RootFolder != null)
            {
                InventoryItemBase folder = userInfo.RootFolder.FindItem(folderID);

                if (folder != null)
                {
                    m_log.WarnFormat(
                         "[AGENT INVENTORY]: Remove folder not implemented in request by {0} {1} for {2}",
                         remoteClient.Name, remoteClient.AgentId, folderID);

                    // doesn't work just yet, commented out. will fix in next patch.
                    // userInfo.DeleteItem(folder);
                }
            }
        }

        private SceneObjectGroup GetGroupByPrim(uint localID)
        {
            List<EntityBase> EntityList = GetEntities();

            foreach (EntityBase ent in EntityList)
            {
                if (ent is SceneObjectGroup)
                {
                    if (((SceneObjectGroup) ent).HasChildPrim(localID))
                        return (SceneObjectGroup) ent;
                }
            }
            return null;
        }

        /// <summary>
        /// Send the details of a prim's inventory to the client.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="primLocalID"></param>
        public void RequestTaskInventory(IClientAPI remoteClient, uint primLocalID)
        {
            SceneObjectGroup group = GetGroupByPrim(primLocalID);
            if (group != null)
            {
                bool fileChange = group.GetPartInventoryFileName(remoteClient, primLocalID);
                if (fileChange)
                {
                    if (XferManager != null)
                    {
                        group.RequestInventoryFile(remoteClient, primLocalID, XferManager);
                    }
                }
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Inventory requested of prim {0} which doesn't exist", primLocalID);
            }
        }

        /// <summary>
        /// Remove an item from a prim (task) inventory
        /// </summary>
        /// <param name="remoteClient">Unused at the moment but retained since the avatar ID might
        /// be necessary for a permissions check at some stage.</param>
        /// <param name="itemID"></param>
        /// <param name="localID"></param>
        public void RemoveTaskInventory(IClientAPI remoteClient, UUID itemID, uint localID)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            SceneObjectGroup group = part.ParentGroup;
            if (group != null)
            {
                TaskInventoryItem item = group.GetInventoryItem(localID, itemID);
                if (item == null)
                    return;

                if (item.Type == 10)
                {
                    EventManager.TriggerRemoveScript(localID, itemID);
                }
                group.RemoveInventoryItem(localID, itemID);
                part.GetProperties(remoteClient);
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Removal of item {0} requested of prim {1} but this prim does not exist",
                    itemID,
                    localID);
            }
        }

        private InventoryItemBase CreateAgentInventoryItemFromTask(UUID destAgent, SceneObjectPart part, UUID itemId)
        {
            TaskInventoryItem taskItem = part.Inventory.GetInventoryItem(itemId);

            if (null == taskItem)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for creating an avatar"
                        + " inventory item from a prim's inventory item "
                        + " but the required item does not exist in the prim's inventory",
                    itemId, part.Name, part.UUID);

                return null;
            }

            if ((destAgent != taskItem.OwnerID) && ((taskItem.CurrentPermissions & (uint)PermissionMask.Transfer) == 0))
            {
                return null;
            }

            InventoryItemBase agentItem = new InventoryItemBase();

            agentItem.ID = UUID.Random();
            agentItem.CreatorId = taskItem.CreatorID.ToString();
            agentItem.Owner = destAgent;
            agentItem.AssetID = taskItem.AssetID;
            agentItem.Description = taskItem.Description;
            agentItem.Name = taskItem.Name;
            agentItem.AssetType = taskItem.Type;
            agentItem.InvType = taskItem.InvType;
            agentItem.Flags = taskItem.Flags;

            if ((part.OwnerID != destAgent) && Permissions.PropagatePermissions())
            {
                if (taskItem.InvType == (int)InventoryType.Object)
                    agentItem.BasePermissions = taskItem.BasePermissions & ((taskItem.CurrentPermissions & 7) << 13);
                else
                    agentItem.BasePermissions = taskItem.BasePermissions;
                agentItem.BasePermissions &= taskItem.NextPermissions;
                agentItem.CurrentPermissions = agentItem.BasePermissions | 8;
                agentItem.NextPermissions = taskItem.NextPermissions;
                agentItem.EveryOnePermissions = taskItem.EveryonePermissions & taskItem.NextPermissions;
                agentItem.GroupPermissions = taskItem.GroupPermissions & taskItem.NextPermissions;
            }
            else
            {
                agentItem.BasePermissions = taskItem.BasePermissions;
                agentItem.CurrentPermissions = taskItem.CurrentPermissions;
                agentItem.NextPermissions = taskItem.NextPermissions;
                agentItem.EveryOnePermissions = taskItem.EveryonePermissions;
                agentItem.GroupPermissions = taskItem.GroupPermissions;
            }

            if (!Permissions.BypassPermissions())
            {
                if ((taskItem.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                    part.Inventory.RemoveInventoryItem(itemId);
            }

            return agentItem;
        }

        /// <summary>
        /// Move the given item in the given prim to a folder in the client's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="part"></param>
        /// <param name="itemID"></param>
        public InventoryItemBase MoveTaskInventoryItem(IClientAPI remoteClient, UUID folderId, SceneObjectPart part, UUID itemId)
        {
            InventoryItemBase agentItem = CreateAgentInventoryItemFromTask(remoteClient.AgentId, part, itemId);

            if (agentItem == null)
                return null;

            agentItem.Folder = folderId;
            AddInventoryItem(remoteClient, agentItem);
            return agentItem;
        }

        /// <summary>
        /// <see>ClientMoveTaskInventoryItem</see>
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="primLocalID"></param>
        /// <param name="itemID"></param>
        public void ClientMoveTaskInventoryItem(IClientAPI remoteClient, UUID folderId, uint primLocalId, UUID itemId)
        {
            SceneObjectPart part = GetSceneObjectPart(primLocalId);

            if (null == part)
            {
                m_log.WarnFormat(
                    "[PRIM INVENTORY]: " +
                    "Move of inventory item {0} from prim with local id {1} failed because the prim could not be found",
                    itemId, primLocalId);

                return;
            }

            TaskInventoryItem taskItem = part.Inventory.GetInventoryItem(itemId);

            if (null == taskItem)
            {
                m_log.WarnFormat("[PRIM INVENTORY]: Move of inventory item {0} from prim with local id {1} failed"
                    + " because the inventory item could not be found",
                    itemId, primLocalId);

                return;
            }

            // Only owner can copy
            if (remoteClient.AgentId != taskItem.OwnerID)
                return;

            MoveTaskInventoryItem(remoteClient, folderId, part, itemId);
        }

        /// <summary>
        /// <see>MoveTaskInventoryItem</see>
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="part"></param>
        /// <param name="itemID"></param>
        public InventoryItemBase MoveTaskInventoryItem(UUID avatarId, UUID folderId, SceneObjectPart part, UUID itemId)
        {
            ScenePresence avatar;

            if (TryGetAvatar(avatarId, out avatar))
            {
                return MoveTaskInventoryItem(avatar.ControllingClient, folderId, part, itemId);
            }
            else
            {
                CachedUserInfo profile = CommsManager.UserProfileCacheService.GetUserDetails(avatarId);
                if (profile == null || profile.RootFolder == null)
                {
                    m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Avatar {0} cannot be found to add item",
                        avatarId);
                    return null;
                }
                
                if (!profile.HasReceivedInventory)
                    profile.FetchInventory();
                
                InventoryItemBase agentItem = CreateAgentInventoryItemFromTask(avatarId, part, itemId);

                if (agentItem == null)
                    return null;

                agentItem.Folder = folderId;

                AddInventoryItem(avatarId, agentItem);

                return agentItem;
            }
        }

        /// <summary>
        /// Copy a task (prim) inventory item to another task (prim)
        /// </summary>
        /// <param name="destId"></param>
        /// <param name="part"></param>
        /// <param name="itemId"></param>
        public void MoveTaskInventoryItem(UUID destId, SceneObjectPart part, UUID itemId)
        {
            TaskInventoryItem srcTaskItem = part.Inventory.GetInventoryItem(itemId);

            if (srcTaskItem == null)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for moving"
                        + " but the item does not exist in this inventory",
                    itemId, part.Name, part.UUID);

                return;
            }

            SceneObjectPart destPart = GetSceneObjectPart(destId);

            if (destPart == null)
            {
                m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Could not find prim for ID {0}",
                        destId);
                return;
            }

            // Can't transfer this
            //
            if ((part.OwnerID != destPart.OwnerID) && ((srcTaskItem.CurrentPermissions & (uint)PermissionMask.Transfer) == 0))
                return;

            if (part.OwnerID != destPart.OwnerID && (part.GetEffectiveObjectFlags() & (uint)PrimFlags.AllowInventoryDrop) == 0)
            {
                // object cannot copy items to an object owned by a different owner
                // unless llAllowInventoryDrop has been called

                return;
            }

            // must have both move and modify permission to put an item in an object
            if ((part.OwnerMask & ((uint)PermissionMask.Move | (uint)PermissionMask.Modify)) == 0)
            {
                return;
            }

            TaskInventoryItem destTaskItem = new TaskInventoryItem();

            destTaskItem.ItemID = UUID.Random();
            destTaskItem.CreatorID = srcTaskItem.CreatorID;
            destTaskItem.AssetID = srcTaskItem.AssetID;
            destTaskItem.GroupID = destPart.GroupID;
            destTaskItem.OwnerID = destPart.OwnerID;
            destTaskItem.ParentID = destPart.UUID;
            destTaskItem.ParentPartID = destPart.UUID;

            destTaskItem.BasePermissions = srcTaskItem.BasePermissions;
            destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions;
            destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions;
            destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions;
            destTaskItem.NextPermissions = srcTaskItem.NextPermissions;
            destTaskItem.Flags = srcTaskItem.Flags;

            if (destPart.OwnerID != part.OwnerID)
            {
                if (Permissions.PropagatePermissions())
                {
                    destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.BasePermissions = srcTaskItem.BasePermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.CurrentPermissions |= 8; // Slam!
                }
            }

            destTaskItem.Description = srcTaskItem.Description;
            destTaskItem.Name = srcTaskItem.Name;
            destTaskItem.InvType = srcTaskItem.InvType;
            destTaskItem.Type = srcTaskItem.Type;

            destPart.Inventory.AddInventoryItem(destTaskItem, part.OwnerID != destPart.OwnerID);

            if ((srcTaskItem.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                part.Inventory.RemoveInventoryItem(itemId);

            ScenePresence avatar;

            if (TryGetAvatar(srcTaskItem.OwnerID, out avatar))
            {
                destPart.GetProperties(avatar.ControllingClient);
            }
        }

        public UUID MoveTaskInventoryItems(UUID destID, string category, SceneObjectPart host, List<UUID> items)
        {
            CachedUserInfo profile = CommsManager.UserProfileCacheService.GetUserDetails(destID);
            if (profile == null || profile.RootFolder == null)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Avatar {0} cannot be found to add items",
                    destID);
                return UUID.Zero;
            }

            UUID newFolderID = UUID.Random();

            profile.CreateFolder(category, newFolderID, 0xffff, profile.RootFolder.ID);

            foreach (UUID itemID in items)
            {
                InventoryItemBase agentItem = CreateAgentInventoryItemFromTask(destID, host, itemID);

                if (agentItem != null)
                {
                    agentItem.Folder = newFolderID;

                    AddInventoryItem(destID, agentItem);
                }
            }

            ScenePresence avatar;

            if (TryGetAvatar(destID, out avatar))
            {
                profile.SendInventoryDecendents(avatar.ControllingClient,
                        profile.RootFolder.ID, true, false);
                profile.SendInventoryDecendents(avatar.ControllingClient,
                        newFolderID, false, true);
            }

            return newFolderID;
        }

        /// <summary>
        /// Update an item in a prim (task) inventory.
        /// This method does not handle scripts, <see>RezScript(IClientAPI, UUID, unit)</see>
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="transactionID"></param>
        /// <param name="itemInfo"></param>
        /// <param name="primLocalID"></param>
        public void UpdateTaskInventory(IClientAPI remoteClient, UUID transactionID, TaskInventoryItem itemInfo,
                                        uint primLocalID)
        {
            UUID itemID = itemInfo.ItemID;

            // Find the prim we're dealing with
            SceneObjectPart part = GetSceneObjectPart(primLocalID);

            if (part != null)
            {
                TaskInventoryItem currentItem = part.Inventory.GetInventoryItem(itemID);
                bool allowInventoryDrop = (part.GetEffectiveObjectFlags()
                                           & (uint)PrimFlags.AllowInventoryDrop) != 0;

                // Explicity allow anyone to add to the inventory if the
                // AllowInventoryDrop flag has been set. Don't however let
                // them update an item unless they pass the external checks
                //
                if (!Permissions.CanEditObjectInventory(part.UUID, remoteClient.AgentId)
                    && (currentItem != null || !allowInventoryDrop))
                    return;

                if (currentItem == null)
                {
                    UUID copyID = UUID.Random();
                    if (itemID != UUID.Zero)
                    {
                        CachedUserInfo userInfo = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

                        if (userInfo != null && userInfo.RootFolder != null)
                        {
                            InventoryItemBase item = userInfo.RootFolder.FindItem(itemID);

                            // Try library
                            // XXX clumsy, possibly should be one call
                            if (null == item)
                            {
                                item = CommsManager.UserProfileCacheService.LibraryRoot.FindItem(itemID);
                            }

                            if (item != null)
                            {
                                part.ParentGroup.AddInventoryItem(remoteClient, primLocalID, item, copyID);
                                m_log.InfoFormat(
                                    "[PRIM INVENTORY]: Update with item {0} requested of prim {1} for {2}",
                                    item.Name, primLocalID, remoteClient.Name);
                                part.GetProperties(remoteClient);
                                if (!Permissions.BypassPermissions())
                                {
                                    if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                                        RemoveInventoryItem(remoteClient, itemID);
                                }
                            }
                            else
                            {
                                m_log.ErrorFormat(
                                    "[PRIM INVENTORY]: Could not find inventory item {0} to update for {1}!",
                                    itemID, remoteClient.Name);
                            }
                        }
                    }
                }
                else // Updating existing item with new perms etc
                {
                    IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
                    if (agentTransactions != null)
                    {
                        agentTransactions.HandleTaskItemUpdateFromTransaction(
                            remoteClient, part, transactionID, currentItem);
                    }
                    if (part.Inventory.UpdateInventoryItem(itemInfo))
                        part.GetProperties(remoteClient);
                }
            }
            else
            {
                m_log.WarnFormat(
                    "[PRIM INVENTORY]: " +
                    "Update with item {0} requested of prim {1} for {2} but this prim does not exist",
                    itemID, primLocalID, remoteClient.Name);
            }
        }

        /// <summary>
        /// Rez a script into a prim's inventory, either ex nihilo or from an existing avatar inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"> </param>
        /// <param name="localID"></param>
        public void RezScript(IClientAPI remoteClient, InventoryItemBase itemBase, UUID transactionID, uint localID)
        {
            UUID itemID = itemBase.ID;
            UUID copyID = UUID.Random();

            if (itemID != UUID.Zero)  // transferred from an avatar inventory to the prim's inventory
            {
                CachedUserInfo userInfo = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

                if (userInfo != null && userInfo.RootFolder != null)
                {
                    InventoryItemBase item = userInfo.RootFolder.FindItem(itemID);

                    // Try library
                    // XXX clumsy, possibly should be one call
                    if (null == item)
                    {
                        item = CommsManager.UserProfileCacheService.LibraryRoot.FindItem(itemID);
                    }

                    if (item != null)
                    {
                        SceneObjectPart part = GetSceneObjectPart(localID);
                        if (part != null)
                        {
                            if (!Permissions.CanEditObjectInventory(part.UUID, remoteClient.AgentId))
                                return;

                            part.ParentGroup.AddInventoryItem(remoteClient, localID, item, copyID);
                            // TODO: switch to posting on_rez here when scripts
                            // have state in inventory
                            part.Inventory.CreateScriptInstance(copyID, 0, false, DefaultScriptEngine, 0);

                            //                        m_log.InfoFormat("[PRIMINVENTORY]: " +
                            //                                         "Rezzed script {0} into prim local ID {1} for user {2}",
                            //                                         item.inventoryName, localID, remoteClient.Name);
                            part.GetProperties(remoteClient);
                        }
                        else
                        {
                            m_log.ErrorFormat(
                                "[PRIM INVENTORY]: " +
                                "Could not rez script {0} into prim local ID {1} for user {2}"
                                + " because the prim could not be found in the region!",
                                item.Name, localID, remoteClient.Name);
                        }
                    }
                    else
                    {
                        m_log.ErrorFormat(
                            "[PRIM INVENTORY]: Could not find script inventory item {0} to rez for {1}!",
                            itemID, remoteClient.Name);
                    }
                }
            }
            else  // script has been rezzed directly into a prim's inventory
            {
                SceneObjectPart part = GetSceneObjectPart(itemBase.Folder);
                if (part == null)
                    return;

                if (part.OwnerID != remoteClient.AgentId)
                    return;

                if ((part.OwnerMask & (uint)PermissionMask.Modify) == 0)
                    return;

                if (!Permissions.CanCreateObjectInventory(
                    itemBase.InvType, part.UUID, remoteClient.AgentId))
                    return;

                AssetBase asset = CreateAsset(itemBase.Name, itemBase.Description, (sbyte)itemBase.AssetType, Encoding.ASCII.GetBytes("default\n{\n    state_entry()\n    {\n        llSay(0, \"Script running\");\n    }\n}"));
                CommsManager.AssetCache.AddAsset(asset);

                TaskInventoryItem taskItem = new TaskInventoryItem();

                taskItem.ResetIDs(itemBase.Folder);
                taskItem.ParentID = itemBase.Folder;
                taskItem.CreationDate = (uint)itemBase.CreationDate;
                taskItem.Name = itemBase.Name;
                taskItem.Description = itemBase.Description;
                taskItem.Type = itemBase.AssetType;
                taskItem.InvType = itemBase.InvType;
                taskItem.OwnerID = itemBase.Owner;
                taskItem.CreatorID = itemBase.CreatorIdAsUuid;
                taskItem.BasePermissions = itemBase.BasePermissions;
                taskItem.CurrentPermissions = itemBase.CurrentPermissions;
                taskItem.EveryonePermissions = itemBase.EveryOnePermissions;
                taskItem.GroupPermissions = itemBase.GroupPermissions;
                taskItem.NextPermissions = itemBase.NextPermissions;
                taskItem.GroupID = itemBase.GroupID;
                taskItem.GroupPermissions = 0;
                taskItem.Flags = itemBase.Flags;
                taskItem.PermsGranter = UUID.Zero;
                taskItem.PermsMask = 0;
                taskItem.AssetID = asset.FullID;

                part.Inventory.AddInventoryItem(taskItem, false);
                part.GetProperties(remoteClient);

                part.Inventory.CreateScriptInstance(taskItem, 0, false, DefaultScriptEngine, 0);
            }
        }

        /// <summary>
        /// Rez a script into a prim's inventory from another prim
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"> </param>
        /// <param name="localID"></param>
        public void RezScript(UUID srcId, SceneObjectPart srcPart, UUID destId, int pin, int running, int start_param)
        {
            TaskInventoryItem srcTaskItem = srcPart.Inventory.GetInventoryItem(srcId);

            if (srcTaskItem == null)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for rezzing a script but the "
                        + " item does not exist in this inventory",
                    srcId, srcPart.Name, srcPart.UUID);

                return;
            }

            SceneObjectPart destPart = GetSceneObjectPart(destId);

            if (destPart == null)
            {
                m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Could not find script for ID {0}",
                        destId);
                return;
            }

            // Must own the object, and have modify rights
            if (srcPart.OwnerID != destPart.OwnerID)
                return;

            if ((destPart.OwnerMask & (uint)PermissionMask.Modify) == 0)
                return;

            if (destPart.ScriptAccessPin != pin)
            {
                m_log.WarnFormat(
                        "[PRIM INVENTORY]: " +
                        "Script in object {0} : {1}, attempted to load script {2} : {3} into object {4} : {5} with invalid pin {6}",
                        srcPart.Name, srcId, srcTaskItem.Name, srcTaskItem.ItemID, destPart.Name, destId, pin);
                // the LSL Wiki says we are supposed to shout on the DEBUG_CHANNEL -
                //   "Object: Task Object trying to illegally load script onto task Other_Object!"
                // How do we shout from in here?
                return;
            }

            TaskInventoryItem destTaskItem = new TaskInventoryItem();

            destTaskItem.ItemID = UUID.Random();
            destTaskItem.CreatorID = srcTaskItem.CreatorID;
            destTaskItem.AssetID = srcTaskItem.AssetID;
            destTaskItem.GroupID = destPart.GroupID;
            destTaskItem.OwnerID = destPart.OwnerID;
            destTaskItem.ParentID = destPart.UUID;
            destTaskItem.ParentPartID = destPart.UUID;

            destTaskItem.BasePermissions = srcTaskItem.BasePermissions;
            destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions;
            destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions;
            destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions;
            destTaskItem.NextPermissions = srcTaskItem.NextPermissions;
            destTaskItem.Flags = srcTaskItem.Flags;

            if (destPart.OwnerID != srcPart.OwnerID)
            {
                if (Permissions.PropagatePermissions())
                {
                    destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.BasePermissions = srcTaskItem.BasePermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.CurrentPermissions |= 8; // Slam!
                }
            }

            destTaskItem.Description = srcTaskItem.Description;
            destTaskItem.Name = srcTaskItem.Name;
            destTaskItem.InvType = srcTaskItem.InvType;
            destTaskItem.Type = srcTaskItem.Type;

            destPart.Inventory.AddInventoryItemExclusive(destTaskItem, false);

            if (running > 0)
            {
                destPart.Inventory.CreateScriptInstance(destTaskItem, start_param, false, DefaultScriptEngine, 0);
            }

            ScenePresence avatar;

            if (TryGetAvatar(srcTaskItem.OwnerID, out avatar))
            {
                destPart.GetProperties(avatar.ControllingClient);
            }
        }

        /// <summary>
        /// Called when an object is removed from the environment into inventory.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="localID"></param>
        /// <param name="groupID"></param>
        /// <param name="action"></param>
        /// <param name="destinationID"></param>
        public virtual void DeRezObject(IClientAPI remoteClient, uint localID,
                UUID groupID, DeRezAction action, UUID destinationID)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part == null)
                return;

            if (part.ParentGroup == null || part.ParentGroup.IsDeleted)
                return;

            // Can't delete child prims
            if (part != part.ParentGroup.RootPart)
                return;

            SceneObjectGroup grp = part.ParentGroup;

            //force a database backup/update on this SceneObjectGroup
            //So that we know the database is upto date, for when deleting the object from it
            ForceSceneObjectBackup(grp);

            bool permissionToTake = false;
            bool permissionToDelete = false;

            if (action == DeRezAction.SaveToExistingUserInventoryItem)
            {
                if (grp.OwnerID == remoteClient.AgentId && grp.RootPart.FromUserInventoryItemID != UUID.Zero)
                {
                    permissionToTake = true;
                    permissionToDelete = false;
                }
            }
            else if (action == DeRezAction.TakeCopy)
            {
                permissionToTake =
                        Permissions.CanTakeCopyObject(
                        grp.UUID,
                        remoteClient.AgentId);
            }
            else if (action == DeRezAction.GodTakeCopy)
            {
                permissionToTake =
                        Permissions.IsGod(
                        remoteClient.AgentId);
            }
            else if (action == DeRezAction.Take)
            {
                permissionToTake =
                        Permissions.CanTakeObject(
                        grp.UUID,
                        remoteClient.AgentId);

                //If they can take, they can delete!
                permissionToDelete = permissionToTake;
            }
            else if (action == DeRezAction.Delete)
            {
                permissionToTake =
                        Permissions.CanDeleteObject(
                        grp.UUID,
                        remoteClient.AgentId);
                permissionToDelete = permissionToTake;
            }
            else if (action == DeRezAction.Return)
            {
                if (remoteClient != null)
                {
                    permissionToTake =
                            Permissions.CanReturnObject(
                            grp.UUID,
                            remoteClient.AgentId);
                    permissionToDelete = permissionToTake;

                    if (permissionToDelete)
                    {
                        AddReturn(grp.OwnerID, grp.Name, grp.AbsolutePosition, "parcel owner return");
                    }
                }
                else // Auto return passes through here with null agent
                {
                    permissionToTake = true;
                    permissionToDelete = true;
                }
            }
            else
            {
                m_log.DebugFormat(
                    "[AGENT INVENTORY]: Ignoring unexpected derez action {0} for {1}", action, remoteClient.Name);
                return;
            }

            if (permissionToTake)
            {
                m_asyncSceneObjectDeleter.DeleteToInventory(
                        action, destinationID, grp, remoteClient,
                        permissionToDelete);
            }
            else if (permissionToDelete)
            {
                DeleteSceneObject(grp, false);
            }
        }

        private bool WaitForInventory(CachedUserInfo info)
        {
            // 200 Seconds wait. This is called in the context of the
            // background delete thread, so we can afford to waste time
            // here.
            //
            int count = 200;

            while (count > 0)
            {
                System.Threading.Thread.Sleep(100);
                count--;
                if (info.HasReceivedInventory)
                    return true;
            }
            m_log.DebugFormat("Timed out waiting for inventory of user {0}",
                    info.UserProfile.ID.ToString());
            return false;
        }

        /// <summary>
        /// Delete a scene object from a scene and place in the given avatar's inventory.
        /// Returns the UUID of the newly created asset.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="folderID"></param>
        /// <param name="objectGroup"></param>
        /// <param name="remoteClient"> </param>
        public virtual UUID DeleteToInventory(DeRezAction action, UUID folderID,
                SceneObjectGroup objectGroup, IClientAPI remoteClient)
        {
            UUID assetID = UUID.Zero;

            string sceneObjectXml = SceneObjectSerializer.ToOriginalXmlFormat(objectGroup);

            // Get the user info of the item destination
            //
            CachedUserInfo userInfo;

            if (action == DeRezAction.Take || action == DeRezAction.TakeCopy ||
                action == DeRezAction.SaveToExistingUserInventoryItem)
            {
                // Take or take copy require a taker
                // Saving changes requires a local user
                //
                if (remoteClient == null)
                    return UUID.Zero;

                userInfo = CommsManager.UserProfileCacheService.GetUserDetails(
                        remoteClient.AgentId);
            }
            else
            {
                // All returns / deletes go to the object owner
                //
                userInfo = CommsManager.UserProfileCacheService.GetUserDetails(
                        objectGroup.RootPart.OwnerID);
            }

            if (userInfo == null) // Can't proceed
            {
                return UUID.Zero;
            }

            if (!userInfo.HasReceivedInventory)
            {
                // Async inventory requests will queue, but they will never
                // execute unless inventory is actually fetched
                //
                userInfo.FetchInventory();
            }

            // If we're returning someone's item, it goes back to the
            // owner's Lost And Found folder.
            // Delete is treated like return in this case
            // Deleting your own items makes them go to trash
            //

            InventoryFolderBase folder = null;
            InventoryItemBase item = null;

            if (DeRezAction.SaveToExistingUserInventoryItem == action)
            {
                item = userInfo.RootFolder.FindItem(
                        objectGroup.RootPart.FromUserInventoryItemID);

                if (null == item)
                {
                    m_log.DebugFormat(
                        "[AGENT INVENTORY]: Object {0} {1} scheduled for save to inventory has already been deleted.",
                        objectGroup.Name, objectGroup.UUID);
                    return UUID.Zero;
                }
            }
            else
            {
                // Folder magic
                //
                if (action == DeRezAction.Delete)
                {
                    // Deleting someone else's item
                    //
                    if (remoteClient == null ||
                        objectGroup.OwnerID != remoteClient.AgentId)
                    {
                        // Folder skeleton may not be loaded and we
                        // have to wait for the inventory to find
                        // the destination folder
                        //
                        if (!WaitForInventory(userInfo))
                            return UUID.Zero;
                        folder = userInfo.FindFolderForType(
                                (int)AssetType.LostAndFoundFolder);
                    }
                    else
                    {
                        // Assume inventory skeleton was loaded during login
                        // and all folders can be found
                        //
                        folder = userInfo.FindFolderForType(
                                (int)AssetType.TrashFolder);
                    }
                }
                else if (action == DeRezAction.Return)
                {
                    // Wait if needed
                    //
                    if (!userInfo.HasReceivedInventory)
                    {
                        if (!WaitForInventory(userInfo))
                            return UUID.Zero;
                    }

                    // Dump to lost + found unconditionally
                    //
                    folder = userInfo.FindFolderForType(
                            (int)AssetType.LostAndFoundFolder);
                }

                if (folderID == UUID.Zero && folder == null)
                {
                    // Catch all. Use lost & found
                    //
                    if (!userInfo.HasReceivedInventory)
                    {
                        if (!WaitForInventory(userInfo))
                            return UUID.Zero;
                    }

                    folder = userInfo.FindFolderForType(
                            (int)AssetType.LostAndFoundFolder);
                }

                if (folder == null) // None of the above
                {
                    folder = userInfo.RootFolder.FindFolder(folderID);

                    if (folder == null) // Nowhere to put it
                    {
                        return UUID.Zero;
                    }
                }

                item = new InventoryItemBase();
                item.CreatorId = objectGroup.RootPart.CreatorID.ToString();
                item.ID = UUID.Random();
                item.InvType = (int)InventoryType.Object;
                item.Folder = folder.ID;
                item.Owner = userInfo.UserProfile.ID;
            }

            AssetBase asset = CreateAsset(
                objectGroup.GetPartName(objectGroup.RootPart.LocalId),
                objectGroup.GetPartDescription(objectGroup.RootPart.LocalId),
                (sbyte)AssetType.Object,
                Utils.StringToBytes(sceneObjectXml));
            CommsManager.AssetCache.AddAsset(asset);
            assetID = asset.FullID;

            if (DeRezAction.SaveToExistingUserInventoryItem == action)
            {
                item.AssetID = asset.FullID;
                userInfo.UpdateItem(item);
            }
            else
            {
                item.AssetID = asset.FullID;

                if (remoteClient != null && (remoteClient.AgentId != objectGroup.RootPart.OwnerID) && Permissions.PropagatePermissions())
                {
                    uint perms=objectGroup.GetEffectivePermissions();
                    uint nextPerms=(perms & 7) << 13;
                    if ((nextPerms & (uint)PermissionMask.Copy) == 0)
                        perms &= ~(uint)PermissionMask.Copy;
                    if ((nextPerms & (uint)PermissionMask.Transfer) == 0)
                        perms &= ~(uint)PermissionMask.Transfer;
                    if ((nextPerms & (uint)PermissionMask.Modify) == 0)
                        perms &= ~(uint)PermissionMask.Modify;

                    item.BasePermissions = perms & objectGroup.RootPart.NextOwnerMask;
                    item.CurrentPermissions = item.BasePermissions;
                    item.NextPermissions = objectGroup.RootPart.NextOwnerMask;
                    item.EveryOnePermissions = objectGroup.RootPart.EveryoneMask & objectGroup.RootPart.NextOwnerMask;
                    item.GroupPermissions = objectGroup.RootPart.GroupMask & objectGroup.RootPart.NextOwnerMask;
                    item.CurrentPermissions |= 8; // Slam!
                }
                else
                {
                    item.BasePermissions = objectGroup.GetEffectivePermissions();
                    item.CurrentPermissions = objectGroup.GetEffectivePermissions();
                    item.NextPermissions = objectGroup.RootPart.NextOwnerMask;
                    item.EveryOnePermissions = objectGroup.RootPart.EveryoneMask;
                    item.GroupPermissions = objectGroup.RootPart.GroupMask;

                    item.CurrentPermissions |= 8; // Slam!
                }

                // TODO: add the new fields (Flags, Sale info, etc)
                item.CreationDate = Util.UnixTimeSinceEpoch();
                item.Description = asset.Description;
                item.Name = asset.Name;
                item.AssetType = asset.Type;

                userInfo.AddItem(item);

                if (remoteClient != null && item.Owner == remoteClient.AgentId)
                {
                    remoteClient.SendInventoryItemCreateUpdate(item, 0);
                }
                else
                {
                    ScenePresence notifyUser = GetScenePresence(item.Owner);
                    if (notifyUser != null)
                    {
                        notifyUser.ControllingClient.SendInventoryItemCreateUpdate(item, 0);
                    }
                }
            }

            return assetID;
        }

        public void updateKnownAsset(IClientAPI remoteClient, SceneObjectGroup grp, UUID assetID, UUID agentID)
        {
            SceneObjectGroup objectGroup = grp;
            if (objectGroup != null)
            {
                if (!grp.HasGroupChanged)
                {
                    m_log.InfoFormat("[ATTACHMENT]: Save request for {0} which is unchanged", grp.UUID);
                    return;
                }

                m_log.InfoFormat(
                    "[ATTACHMENT]: Updating asset for attachment {0}, attachpoint {1}",
                    grp.UUID, grp.GetAttachmentPoint());

                string sceneObjectXml = SceneObjectSerializer.ToOriginalXmlFormat(objectGroup);

                CachedUserInfo userInfo =
                    CommsManager.UserProfileCacheService.GetUserDetails(agentID);
                if (userInfo != null && userInfo.RootFolder != null)
                {
                    Queue<InventoryFolderImpl> searchfolders = new Queue<InventoryFolderImpl>();
                    searchfolders.Enqueue(userInfo.RootFolder);

                    UUID foundFolder = UUID.Zero;
                    InventoryItemBase item = null;

                    // search through folders to find the asset.
                    while (searchfolders.Count > 0)
                    {
                        InventoryFolderImpl fld = searchfolders.Dequeue();
                        lock (fld)
                        {
                            if (fld != null)
                            {
                                if (fld.Items.ContainsKey(assetID))
                                {
                                    item = fld.Items[assetID];
                                    foundFolder = fld.ID;
                                    searchfolders.Clear();
                                    break;
                                }
                                else
                                {
                                    foreach (InventoryFolderImpl subfld in fld.RequestListOfFolderImpls())
                                    {
                                        searchfolders.Enqueue(subfld);
                                    }
                                }
                            }
                        }
                    }

                    if (foundFolder != UUID.Zero && item != null)
                    {
                        AssetBase asset = CreateAsset(
                            objectGroup.GetPartName(objectGroup.LocalId),
                            objectGroup.GetPartDescription(objectGroup.LocalId),
                            (sbyte)AssetType.Object,
                            Utils.StringToBytes(sceneObjectXml));
                        CommsManager.AssetCache.AddAsset(asset);

                        item.AssetID = asset.FullID;
                        item.Description = asset.Description;
                        item.Name = asset.Name;
                        item.AssetType = asset.Type;
                        item.InvType = (int)InventoryType.Object;
                        item.Folder = foundFolder;

                        userInfo.UpdateItem(item);

                        // this gets called when the agent loggs off!
                        if (remoteClient != null)
                        {
                            remoteClient.SendInventoryItemCreateUpdate(item, 0);
                        }
                    }
                }
            }
        }

        public UUID attachObjectAssetStore(IClientAPI remoteClient, SceneObjectGroup grp, UUID AgentId, out UUID itemID)
        {
            itemID = UUID.Zero;
            if (grp != null)
            {
                string sceneObjectXml = SceneObjectSerializer.ToOriginalXmlFormat(grp);

                CachedUserInfo userInfo =
                    CommsManager.UserProfileCacheService.GetUserDetails(AgentId);
                
                if (userInfo != null)
                {
                    AssetBase asset = CreateAsset(
                        grp.GetPartName(grp.LocalId),
                        grp.GetPartDescription(grp.LocalId),
                        (sbyte)AssetType.Object,
                        Utils.StringToBytes(sceneObjectXml));
                    CommsManager.AssetCache.AddAsset(asset);

                    InventoryItemBase item = new InventoryItemBase();
                    item.CreatorId = grp.RootPart.CreatorID.ToString();
                    item.Owner = remoteClient.AgentId;
                    item.ID = UUID.Random();
                    item.AssetID = asset.FullID;
                    item.Description = asset.Description;
                    item.Name = asset.Name;
                    item.AssetType = asset.Type;
                    item.InvType = (int)InventoryType.Object;

                    item.Folder = UUID.Zero; // Objects folder!

                    if ((remoteClient.AgentId != grp.RootPart.OwnerID) && Permissions.PropagatePermissions())
                    {
                        item.BasePermissions = grp.RootPart.NextOwnerMask;
                        item.CurrentPermissions = grp.RootPart.NextOwnerMask;
                        item.NextPermissions = grp.RootPart.NextOwnerMask;
                        item.EveryOnePermissions = grp.RootPart.EveryoneMask & grp.RootPart.NextOwnerMask;
                        item.GroupPermissions = grp.RootPart.GroupMask & grp.RootPart.NextOwnerMask;
                    }
                    else
                    {
                        item.BasePermissions = grp.RootPart.BaseMask;
                        item.CurrentPermissions = grp.RootPart.OwnerMask;
                        item.NextPermissions = grp.RootPart.NextOwnerMask;
                        item.EveryOnePermissions = grp.RootPart.EveryoneMask;
                        item.GroupPermissions = grp.RootPart.GroupMask;
                    }
                    item.CreationDate = Util.UnixTimeSinceEpoch();

                    // sets assetID so client can show asset as 'attached' in inventory
                    grp.SetFromAssetID(item.ID);

                    userInfo.AddItem(item);
                    remoteClient.SendInventoryItemCreateUpdate(item, 0);

                    itemID = item.ID;
                    return item.AssetID;
                }
                return UUID.Zero;
            }
            return UUID.Zero;
        }

        /// <summary>
        /// Event Handler Rez an object into a scene
        /// Calls the non-void event handler
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="RayEnd"></param>
        /// <param name="RayStart"></param>
        /// <param name="RayTargetID"></param>
        /// <param name="BypassRayCast"></param>
        /// <param name="RayEndIsIntersection"></param>
        /// <param name="EveryoneMask"></param>
        /// <param name="GroupMask"></param>
        /// <param name="RezSelected"></param>
        /// <param name="RemoveItem"></param>
        /// <param name="fromTaskID"></param>
        public virtual void RezObject(IClientAPI remoteClient, UUID itemID, Vector3 RayEnd, Vector3 RayStart,
                                    UUID RayTargetID, byte BypassRayCast, bool RayEndIsIntersection,
                                    bool RezSelected, bool RemoveItem, UUID fromTaskID)
        {
            RezObject(
                remoteClient, itemID, RayEnd, RayStart, RayTargetID, BypassRayCast, RayEndIsIntersection,
                RezSelected, RemoveItem, fromTaskID, false);
        }

        /// <summary>
        /// Rez an object into the scene from the user's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="RayEnd"></param>
        /// <param name="RayStart"></param>
        /// <param name="RayTargetID"></param>
        /// <param name="BypassRayCast"></param>
        /// <param name="RayEndIsIntersection"></param>
        /// <param name="RezSelected"></param>
        /// <param name="RemoveItem"></param>
        /// <param name="fromTaskID"></param>
        /// <param name="attachment"></param>
        /// <returns>The SceneObjectGroup rezzed or null if rez was unsuccessful.</returns>
        public virtual SceneObjectGroup RezObject(IClientAPI remoteClient, UUID itemID, Vector3 RayEnd, Vector3 RayStart,
                                    UUID RayTargetID, byte BypassRayCast, bool RayEndIsIntersection,
                                    bool RezSelected, bool RemoveItem, UUID fromTaskID, bool attachment)
        {
            // Work out position details
            byte bRayEndIsIntersection = (byte)0;

            if (RayEndIsIntersection)
            {
                bRayEndIsIntersection = (byte)1;
            }
            else
            {
                bRayEndIsIntersection = (byte)0;
            }

            Vector3 scale = new Vector3(0.5f, 0.5f, 0.5f);


            Vector3 pos = GetNewRezLocation(
                      RayStart, RayEnd, RayTargetID, Quaternion.Identity,
                      BypassRayCast, bRayEndIsIntersection,true,scale, false);

            // Rez object
            CachedUserInfo userInfo = CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);
            if (userInfo != null)
            {
                // Do NOT use HasReceivedInventory here, this is called
                // from within ItemReceive during login for attachments.
                // Using HasReceivedInventory here will break attachment
                // persistence!
                //
                if (userInfo.RootFolder != null)
                {
                    InventoryItemBase item = userInfo.RootFolder.FindItem(itemID);

                    if (item != null)
                    {
                        AssetBase rezAsset = CommsManager.AssetCache.GetAsset(item.AssetID, false);

                        if (rezAsset != null)
                        {
                            UUID itemId = UUID.Zero;

                            // If we have permission to copy then link the rezzed object back to the user inventory
                            // item that it came from.  This allows us to enable 'save object to inventory'
                            if (!Permissions.BypassPermissions())
                            {
                                if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == (uint)PermissionMask.Copy)
                                {
                                    itemId = item.ID;
                                }
                            }
                            else
                            {
                                // Brave new fullperm world
                                //
                                itemId = item.ID;
                            }

                            string xmlData = Utils.BytesToString(rezAsset.Data);
                            SceneObjectGroup group 
                                = SceneObjectSerializer.FromOriginalXmlFormat(itemId, xmlData);

                            if (!Permissions.CanRezObject(
                                group.Children.Count, remoteClient.AgentId, pos)
                                && !attachment)
                            {
                                return null;
                            }

                            group.ResetIDs();

                            AddNewSceneObject(group, true);

                            // if attachment we set it's asset id so object updates can reflect that
                            // if not, we set it's position in world.
                            if (!attachment)
                            {
                                pos = GetNewRezLocation(
                                    RayStart, RayEnd, RayTargetID, Quaternion.Identity,
                                    BypassRayCast, bRayEndIsIntersection, true, group.GroupScale(), false);
                                group.AbsolutePosition = pos;
                            }
                            else
                            {
                                group.SetFromAssetID(itemID);
                            }

                            SceneObjectPart rootPart = null;
                            try
                            {
                                rootPart = group.GetChildPart(group.UUID);
                            }
                            catch (NullReferenceException)
                            {
                                string isAttachment = "";

                                if (attachment)
                                    isAttachment = " Object was an attachment";

                                m_log.Error("[AGENT INVENTORY]: Error rezzing ItemID: " + itemID + " object has no rootpart." + isAttachment);
                            }

                            // Since renaming the item in the inventory does not affect the name stored
                            // in the serialization, transfer the correct name from the inventory to the
                            // object itself before we rez.
                            rootPart.Name = item.Name;
                            rootPart.Description = item.Description;

                            List<SceneObjectPart> partList = new List<SceneObjectPart>(group.Children.Values);

                            group.SetGroup(remoteClient.ActiveGroupId, remoteClient);
                            if (rootPart.OwnerID != item.Owner)
                            {
                                //Need to kill the for sale here
                                rootPart.ObjectSaleType = 0;
                                rootPart.SalePrice = 10;

                                if (Permissions.PropagatePermissions())
                                {
                                    if ((item.CurrentPermissions & 8) != 0)
                                    {
                                        foreach (SceneObjectPart part in partList)
                                        {
                                            part.EveryoneMask = item.EveryOnePermissions;
                                            part.NextOwnerMask = item.NextPermissions;
                                            part.GroupMask = 0; // DO NOT propagate here
                                        }
                                    }
                                    group.ApplyNextOwnerPermissions();
                                }
                            }

                            foreach (SceneObjectPart part in partList)
                            {
                                if (part.OwnerID != item.Owner)
                                {
                                    part.LastOwnerID = part.OwnerID;
                                    part.OwnerID = item.Owner;
                                    part.Inventory.ChangeInventoryOwner(item.Owner);
                                }
                                else if (((item.CurrentPermissions & 8) != 0) && (!attachment)) // Slam!
                                {
                                    part.EveryoneMask = item.EveryOnePermissions;
                                    part.NextOwnerMask = item.NextPermissions;

                                    part.GroupMask = 0; // DO NOT propagate here
                                }
                            }

                            rootPart.TrimPermissions();

                            if (!attachment)
                            {
                                if (group.RootPart.Shape.PCode == (byte)PCode.Prim)
                                {
                                    group.ClearPartAttachmentData();
                                }
                            }

                            if (!attachment)
                            {
                                // Fire on_rez
                                group.CreateScriptInstances(0, true, DefaultScriptEngine, 0);

                                rootPart.ScheduleFullUpdate();
                            }

                            if (!Permissions.BypassPermissions())
                            {
                                if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                                {
                                    // If this is done on attachments, no
                                    // copy ones will be lost, so avoid it
                                    //
                                    if (!attachment)
                                        userInfo.DeleteItem(item.ID);
                                }
                            }

                            return rootPart.ParentGroup;
                        }
                    }
                }
                else
                    m_log.WarnFormat("[AGENT INVENTORY]: Root folder not found in {0}", RegionInfo.RegionName);
            }
            else
                m_log.WarnFormat("[AGENT INVENTORY]: User profile not found in {0}", RegionInfo.RegionName);

            return null;
        }

        /// <summary>
        /// Rez an object into the scene from a prim's inventory.
        /// </summary>
        /// <param name="sourcePart"></param>
        /// <param name="item"></param>
        /// <param name="pos"></param>
        /// <param name="rot"></param>
        /// <param name="vel"></param>
        /// <param name="param"></param>
        /// <returns>The SceneObjectGroup rezzed or null if rez was unsuccessful</returns>
        public virtual SceneObjectGroup RezObject(
            SceneObjectPart sourcePart, TaskInventoryItem item,
            Vector3 pos, Quaternion rot, Vector3 vel, int param)
        {
            // Rez object
            if (item != null)
            {
                UUID ownerID = item.OwnerID;

                AssetBase rezAsset = CommsManager.AssetCache.GetAsset(item.AssetID, false);

                if (rezAsset != null)
                {
                    string xmlData = Utils.BytesToString(rezAsset.Data);
                    SceneObjectGroup group = SceneObjectSerializer.FromOriginalXmlFormat(xmlData);

                    if (!Permissions.CanRezObject(group.Children.Count, ownerID, pos))
                    {
                        return null;
                    }
                    group.ResetIDs();

                    AddNewSceneObject(group, true);

                    // we set it's position in world.
                    group.AbsolutePosition = pos;

                    SceneObjectPart rootPart = group.GetChildPart(group.UUID);

                    // Since renaming the item in the inventory does not affect the name stored
                    // in the serialization, transfer the correct name from the inventory to the
                    // object itself before we rez.
                    rootPart.Name = item.Name;
                    rootPart.Description = item.Description;

                    List<SceneObjectPart> partList = new List<SceneObjectPart>(group.Children.Values);

                    group.SetGroup(sourcePart.GroupID, null);

                    if (rootPart.OwnerID != item.OwnerID)
                    {
                        if (Permissions.PropagatePermissions())
                        {
                            if ((item.CurrentPermissions & 8) != 0)
                            {
                                foreach (SceneObjectPart part in partList)
                                {
                                    part.EveryoneMask = item.EveryonePermissions;
                                    part.NextOwnerMask = item.NextPermissions;
                                }
                            }
                            group.ApplyNextOwnerPermissions();
                        }
                    }

                    foreach (SceneObjectPart part in partList)
                    {
                        if (part.OwnerID != item.OwnerID)
                        {
                            part.LastOwnerID = part.OwnerID;
                            part.OwnerID = item.OwnerID;
                            part.Inventory.ChangeInventoryOwner(item.OwnerID);
                        }
                        else if ((item.CurrentPermissions & 8) != 0) // Slam!
                        {
                            part.EveryoneMask = item.EveryonePermissions;
                            part.NextOwnerMask = item.NextPermissions;
                        }
                    }
                    
                    rootPart.TrimPermissions();
                    
                    if (group.RootPart.Shape.PCode == (byte)PCode.Prim)
                    {
                        group.ClearPartAttachmentData();
                    }
                    
                    group.UpdateGroupRotation(rot);
                    
                    //group.ApplyPhysics(m_physicalPrim);
                    if (group.RootPart.PhysActor != null && group.RootPart.PhysActor.IsPhysical && vel != Vector3.Zero)
                    {
                        group.RootPart.ApplyImpulse((vel * group.GetMass()), false);
                        group.Velocity = vel;
                        rootPart.ScheduleFullUpdate();
                    }
                    group.CreateScriptInstances(param, true, DefaultScriptEngine, 2);
                    rootPart.ScheduleFullUpdate();

                    if (!Permissions.BypassPermissions())
                    {
                        if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                            sourcePart.Inventory.RemoveInventoryItem(item.ItemID);
                    }
                    return rootPart.ParentGroup;
                }
            }

            return null;
        }

        public virtual bool returnObjects(SceneObjectGroup[] returnobjects, UUID AgentId)
        {
            foreach (SceneObjectGroup grp in returnobjects)
            {
                AddReturn(grp.OwnerID, grp.Name, grp.AbsolutePosition, "parcel owner return");
                DeRezObject(null, grp.RootPart.LocalId,
                        grp.RootPart.GroupID, DeRezAction.Return, UUID.Zero);
            }

            return true;
        }

        public void SetScriptRunning(IClientAPI controllingClient, UUID objectID, UUID itemID, bool running)
        {
            SceneObjectPart part = GetSceneObjectPart(objectID);
            if (part == null)
                return;

            if (running)
                EventManager.TriggerStartScript(part.LocalId, itemID);
            else
                EventManager.TriggerStopScript(part.LocalId, itemID);
        }

        public UUID RezSingleAttachment(IClientAPI remoteClient, UUID itemID,
                uint AttachmentPt)
        {
            SceneObjectGroup att = m_sceneGraph.RezSingleAttachment(remoteClient, itemID, AttachmentPt);

            if (att == null)
            {
                DetachSingleAttachmentToInv(itemID, remoteClient);
                return UUID.Zero;
            }

            return RezSingleAttachment(att, remoteClient, itemID, AttachmentPt);
        }

        public UUID RezSingleAttachment(SceneObjectGroup att,
                IClientAPI remoteClient, UUID itemID, uint AttachmentPt)
        {
            if (!att.IsDeleted)
                AttachmentPt = att.RootPart.AttachmentPoint;

            ScenePresence presence;
            if (TryGetAvatar(remoteClient.AgentId, out presence))
            {
                presence.Appearance.SetAttachment((int)AttachmentPt, itemID, att.UUID);
                IAvatarFactory ava = RequestModuleInterface<IAvatarFactory>();
                if (ava != null)
                {
                    ava.UpdateDatabase(remoteClient.AgentId, presence.Appearance);
                }

            }
            return att.UUID;
        }

        public void RezMultipleAttachments(IClientAPI remoteClient, RezMultipleAttachmentsFromInvPacket.HeaderDataBlock header,
                                       RezMultipleAttachmentsFromInvPacket.ObjectDataBlock[] objects)
        {
            foreach (RezMultipleAttachmentsFromInvPacket.ObjectDataBlock obj in objects)
            {
                RezSingleAttachment(remoteClient, obj.ItemID, obj.AttachmentPt);
            }
        }

        public void AttachObject(IClientAPI controllingClient, uint localID, uint attachPoint, Quaternion rot, Vector3 pos, bool silent)
        {
            m_sceneGraph.AttachObject(controllingClient, localID, attachPoint, rot, pos, silent);
        }

        public void AttachObject(IClientAPI remoteClient, uint AttachmentPt, UUID itemID, SceneObjectGroup att)
        {
            if (UUID.Zero == itemID)
            {
                m_log.Error("[SCENE INVENTORY]: Unable to save attachment. Error inventory item ID.");
                return;
            }

            if (0 == AttachmentPt)
            {
                m_log.Error("[SCENE INVENTORY]: Unable to save attachment. Error attachment point.");
                return;
            }

            if (null == att.RootPart)
            {
                m_log.Error("[SCENE INVENTORY]: Unable to save attachment for a prim without the rootpart!");
                return;
            }

            ScenePresence presence;
            if (TryGetAvatar(remoteClient.AgentId, out presence))
            {
                presence.Appearance.SetAttachment((int)AttachmentPt, itemID, att.UUID);
                IAvatarFactory ava = RequestModuleInterface<IAvatarFactory>();
                if (ava != null)
                {
                    m_log.InfoFormat("[SCENE INVENTORY]: Saving avatar attachment. AgentID:{0} ItemID:{1} AttachmentPoint:{2}", remoteClient.AgentId, itemID, AttachmentPt);
                    ava.UpdateDatabase(remoteClient.AgentId, presence.Appearance);
                }
            }
        }

        public void DetachSingleAttachmentToGround(UUID itemID, IClientAPI remoteClient)
        {
            SceneObjectPart part = GetSceneObjectPart(itemID);
            if (part == null || part.ParentGroup == null)
                return;

            UUID inventoryID = part.ParentGroup.GetFromAssetID();

            ScenePresence presence;
            if (TryGetAvatar(remoteClient.AgentId, out presence))
            {
                if (!Permissions.CanRezObject(part.ParentGroup.Children.Count, remoteClient.AgentId, presence.AbsolutePosition))
                    return;

                presence.Appearance.DetachAttachment(itemID);
                IAvatarFactory ava = RequestModuleInterface<IAvatarFactory>();
                if (ava != null)
                {
                    ava.UpdateDatabase(remoteClient.AgentId, presence.Appearance);
                }
                part.ParentGroup.DetachToGround();
                CachedUserInfo userInfo =
                    CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);
                if (userInfo != null)
                {
                    userInfo.DeleteItem(inventoryID);
                    remoteClient.SendRemoveInventoryItem(inventoryID);
                }
            }
        }

        public void DetachSingleAttachmentToInv(UUID itemID, IClientAPI remoteClient)
        {
            ScenePresence presence;
            if (TryGetAvatar(remoteClient.AgentId, out presence))
            {
                presence.Appearance.DetachAttachment(itemID);
                IAvatarFactory ava = RequestModuleInterface<IAvatarFactory>();
                if (ava != null)
                {
                    ava.UpdateDatabase(remoteClient.AgentId, presence.Appearance);
                }

            }

            m_sceneGraph.DetachSingleAttachmentToInv(itemID, remoteClient);
        }

        public void GetScriptRunning(IClientAPI controllingClient, UUID objectID, UUID itemID)
        {
            EventManager.TriggerGetScriptRunning(controllingClient, objectID, itemID);
        }

        void ObjectOwner(IClientAPI remoteClient, UUID ownerID, UUID groupID, List<uint> localIDs)
        {
            if (!Permissions.IsGod(remoteClient.AgentId))
                return;

            foreach (uint localID in localIDs)
            {
                SceneObjectPart part = GetSceneObjectPart(localID);
                if (part != null && part.ParentGroup != null)
                {
                    part.ParentGroup.SetOwnerId(ownerID);
                    part.Inventory.ChangeInventoryOwner(ownerID);
                    part.ParentGroup.SetGroup(groupID, remoteClient);
                }
            }
        }
    }
}

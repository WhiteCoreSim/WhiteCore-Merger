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

using System.Collections.Generic;
using OpenMetaverse;
using OpenSim.Framework.Communications.Cache;

namespace OpenSim.Framework.Communications
{
    /// <summary>
    /// Callback used when a user's inventory is received from the inventory service
    /// </summary>
    public delegate void InventoryReceiptCallback(
        ICollection<InventoryFolderImpl> folders, ICollection<InventoryItemBase> items);

    /// <summary>
    /// Defines all the operations one can perform on a user's inventory.
    /// </summary>
    public interface IInventoryServices
    {
        string Host
        {
            get;
        }

        /// <summary>
        /// Request the inventory for a user.  This is an asynchronous operation that will call the callback when the
        /// inventory has been received
        /// </summary>
        /// <param name="userID"></param>
        /// <param name="callback"></param>
        void RequestInventoryForUser(UUID userID, InventoryReceiptCallback callback);

        /// <summary>
        /// Add a new folder to the user's inventory
        /// </summary>
        /// <param name="folder"></param>
        /// <returns>true if the folder was successfully added</returns>
        bool AddFolder(InventoryFolderBase folder);

        /// <summary>
        /// Update a folder in the user's inventory
        /// </summary>
        /// <param name="folder"></param>
        /// <returns>true if the folder was successfully updated</returns>
        bool UpdateFolder(InventoryFolderBase folder);

        /// <summary>
        /// Move an inventory folder to a new location
        /// </summary>
        /// <param name="folder">A folder containing the details of the new location</param>
        /// <returns>true if the folder was successfully moved</returns>
        bool MoveFolder(InventoryFolderBase folder);

        /// <summary>
        /// Purge an inventory folder of all its items and subfolders.
        /// </summary>
        /// <param name="folder"></param>
        /// <returns>true if the folder was successfully purged</returns>
        bool PurgeFolder(InventoryFolderBase folder);

        /// <summary>
        /// Add a new item to the user's inventory
        /// </summary>
        /// <param name="item"></param>
        /// <returns>true if the item was successfully added</returns>
        bool AddItem(InventoryItemBase item);

        /// <summary>
        /// Update an item in the user's inventory
        /// </summary>
        /// <param name="item"></param>
        /// <returns>true if the item was successfully updated</returns>
        bool UpdateItem(InventoryItemBase item);

        /// <summary>
        /// Delete an item from the user's inventory
        /// </summary>
        /// <param name="item"></param>
        /// <returns>true if the item was successfully deleted</returns>
        bool DeleteItem(InventoryItemBase item);

        /// <summary>
        /// Query the server for an item that may have been added by
        /// another region
        /// </summary>
        /// <param name="item"></param>
        /// <returns>true if the item was found in local cache</returns>
        InventoryItemBase QueryItem(InventoryItemBase item);

        /// <summary>
        /// Query the server for a folder that may have been added by
        /// another region
        /// </summary>
        /// <param name="folder"></param>
        /// <returns>true if the folder was found in local cache</returns>
        InventoryFolderBase QueryFolder(InventoryFolderBase folder);

        /// <summary>
        /// Does the given user have an inventory structure?
        /// </summary>
        /// <param name="userID"></param>
        /// <returns></returns>
        bool HasInventoryForUser(UUID userID);

        /// <summary>
        /// Retrieve the root inventory folder for the given user.
        /// </summary>
        /// <param name="userID"></param>
        /// <returns>null if no root folder was found</returns>
        InventoryFolderBase RequestRootFolder(UUID userID);
    }
}
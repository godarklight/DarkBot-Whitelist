using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DarkBot;
using Discord.WebSocket;

namespace DarkBot.Whitelist
{
    public class Whitelist : BotModule
    {
        private DiscordSocketClient _client;
        private string dbPath;
        private Dictionary<string, HashSet<ulong>> database = new Dictionary<string, HashSet<ulong>>();

        public Task Initialize(IServiceProvider services)
        {
            dbPath = Path.Combine(Environment.CurrentDirectory, "Whitelist");
            if (!Directory.Exists(dbPath))
            {
                Directory.CreateDirectory(dbPath);
            }
            Load();
            _client = services.GetService(typeof(DiscordSocketClient)) as DiscordSocketClient;
            _client.MessageReceived += MessageReceived;
            return Task.CompletedTask;
        }

        private async Task MessageReceived(SocketMessage message)
        {
            SocketTextChannel stc = message.Channel as SocketTextChannel;
            SocketGuildUser sgu = message.Author as SocketGuildUser;
            if (stc == null || sgu == null || message.Author.IsBot)
            {
                return;
            }
            if (!sgu.GuildPermissions.ManageChannels)
            {
                return;
            }
            if (!message.Content.StartsWith(".whitelist"))
            {
                return;
            }
            await ProcessMessage(stc, message);
        }

        /*Usage:
        .whitelist add key value
        .whitelist remove key value
        .whitelist remove key all
        */
        private async Task ProcessMessage(SocketTextChannel channel, SocketMessage message)
        {
            string[] split = message.Content.Split(' ');
            string returnText = "Error processing command, usage: .whitelist [add|remove] [key] [all|objectID]";
            if (split.Length == 4)
            {
                if (split[1] == "add")
                {
                    if (ulong.TryParse(split[3], out ulong addValue))
                    {
                        returnText = $"Added {split[3]} to {split[2]}";
                        Add(split[2], addValue);
                    }
                }

                if (split[1] == "remove")
                {
                    if (split[3] == "all")
                    {
                        returnText = $"Removed key {split[3]}";
                        Remove(split[2]);
                    }
                    else
                    {
                        if (ulong.TryParse(split[3], out ulong addValue))
                        {
                            returnText = $"Removed {split[3]} from {split[2]}";
                            Remove(split[2], addValue);
                        }
                    }
                }
            }
            await channel.SendMessageAsync(returnText);
        }

        private void Load()
        {
            lock (database)
            {
                database.Clear();
                string[] filesToLoad = Directory.GetFiles(dbPath);
                foreach (string fileToLoad in filesToLoad)
                {
                    string keyName = Path.GetFileNameWithoutExtension(fileToLoad);
                    HashSet<ulong> allowedObjects = new HashSet<ulong>();
                    database.Add(keyName, allowedObjects);
                    using (StreamReader sr = new StreamReader(fileToLoad))
                    {
                        string currentLine = null;
                        while ((currentLine = sr.ReadLine()) != null)
                        {
                            if (ulong.TryParse(currentLine, out ulong addObject))
                            {
                                allowedObjects.Add(addObject);
                            }
                        }
                    }
                }
            }
        }

        private void Save()
        {
            lock (database)
            {
                foreach (KeyValuePair<string, HashSet<ulong>> kvp in database)
                {
                    string savePath = Path.Combine(dbPath, $"{kvp.Key}.txt");
                    using (StreamWriter sw = new StreamWriter(savePath))
                    {
                        foreach (ulong saveID in kvp.Value)
                        {
                            sw.WriteLine(saveID);
                        }
                    }
                }
            }
        }

        private void Remove(string keyName)
        {
            lock (database)
            {
                if (database.ContainsKey(keyName))
                {
                    database.Remove(keyName);
                    File.Delete(Path.Combine(dbPath, $"{keyName}.txt"));
                }
            }
        }

        private void Remove(string keyName, ulong removeID)
        {
            lock (database)
            {
                if (database.ContainsKey(keyName))
                {
                    HashSet<ulong> removeSet = database[keyName];
                    if (removeSet.Contains(removeID))
                    {
                        removeSet.Remove(removeID);
                        if (removeSet.Count == 0)
                        {
                            Remove(keyName);
                        }
                        else
                        {
                            Save();
                        }
                    }
                }
            }
        }

        private void Add(string keyName, ulong addID)
        {
            lock (database)
            {
                HashSet<ulong> keyData;
                if (database.ContainsKey(keyName))
                {
                    keyData = database[keyName];
                }
                else
                {
                    keyData = new HashSet<ulong>();
                    database.Add(keyName, keyData);
                }
                keyData.Add(addID);
                Save();
            }
        }

        public bool ObjectOK(string key, ulong objectID)
        {
            if (database.ContainsKey(key))
            {
                return database[key].Contains(objectID);
            }
            return false;
        }
    }
}

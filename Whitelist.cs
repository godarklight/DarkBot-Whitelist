using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DarkBot;
using Discord;
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
            _client.Ready += SetupCommands;
            _client.SlashCommandExecuted += HandleCommand;
            return Task.CompletedTask;
        }

        private async Task SetupCommands()
        {
            foreach (SocketApplicationCommand sac in await _client.GetGlobalApplicationCommandsAsync())
            {
                if (sac.Name == "whitelist")
                {
                    Log(LogSeverity.Info, "Commands already registered");
                    return;
                }
            }
            Log(LogSeverity.Info, "Setting up commands");
            SlashCommandBuilder scb = new SlashCommandBuilder();
            scb.WithName("whitelist");
            scb.WithDescription("Setup whitelists for other modules");

            List<ChannelType> channelTypes = new List<ChannelType>();
            channelTypes.Add(ChannelType.Text);
            channelTypes.Add(ChannelType.Category);

            SlashCommandOptionBuilder list = new SlashCommandOptionBuilder();
            list.WithName("list");
            list.WithType(ApplicationCommandOptionType.SubCommand);
            list.WithDescription("List whitelists");
            list.AddOption("whitelistname", ApplicationCommandOptionType.String, "Show detail about a whitelist");
            scb.AddOption(list);

            SlashCommandOptionBuilder destroy = new SlashCommandOptionBuilder();
            destroy.WithName("destroy");
            destroy.WithType(ApplicationCommandOptionType.SubCommand);
            destroy.WithDescription("Destroy a whitelist");
            destroy.AddOption("destroylist", ApplicationCommandOptionType.String, "Name of the whitelist to destroy", isRequired: true);
            scb.AddOption(destroy);

            SlashCommandOptionBuilder add = new SlashCommandOptionBuilder();
            add.WithName("add");
            add.WithDescription("Add a channel or category to a whitelist");
            add.WithType(ApplicationCommandOptionType.SubCommand);
            add.AddOption("addselect", ApplicationCommandOptionType.String, "Selected whitelist", isRequired: true);
            add.AddOption("addchannel", ApplicationCommandOptionType.Channel, "Selected channel", isRequired: true, channelTypes: channelTypes);
            scb.AddOption(add);

            SlashCommandOptionBuilder remove = new SlashCommandOptionBuilder();
            remove.WithName("remove");
            remove.WithDescription("Remove a channel or category from a whitelist");
            remove.WithType(ApplicationCommandOptionType.SubCommand);
            remove.AddOption("removeselect", ApplicationCommandOptionType.String, "Selected whitelist", isRequired: true);
            remove.AddOption("removechannel", ApplicationCommandOptionType.Channel, "Selected channel", isRequired: true, channelTypes: channelTypes);
            scb.AddOption(remove);

            await _client.CreateGlobalApplicationCommandAsync(scb.Build());
        }

        private async Task HandleCommand(SocketSlashCommand command)
        {
            if (command.CommandName != "whitelist")
            {
                return;
            }
            SocketGuildChannel sgc = command.Channel as SocketGuildChannel;
            if (sgc == null)
            {
                await command.RespondAsync("This command can only be used from within a guild");
            }
            SocketGuildUser sgu = sgc.GetUser(command.User.Id);
            if (sgu == null)
            {
                await command.RespondAsync("This command can only be used from within a guild");
                return;
            }
            if (!sgu.GuildPermissions.Administrator && !sgu.GuildPermissions.ManageChannels)
            {
                await command.RespondAsync("This command is an admin only command");
                return;
            }
            SocketSlashCommandDataOption opt = command.Data.Options.First<SocketSlashCommandDataOption>();
            if (opt.Name == "destroy")
            {
                SocketSlashCommandDataOption optList = opt.Options.First<SocketSlashCommandDataOption>((x) => { return x.Name == "removeselect"; });
                string list = (string)optList.Value;
                await command.RespondAsync($"Removed {list}");
                Remove(list);
            }
            if (opt.Name == "list")
            {
                SocketSlashCommandDataOption optList = opt.Options.FirstOrDefault<SocketSlashCommandDataOption>();
                string listName = null;
                if (optList != null)
                {
                    listName = (string)optList.Value;
                }
                StringBuilder sb = new StringBuilder();
                if (listName == null || !database.ContainsKey(listName))
                {
                    sb.AppendLine("Current whitelists:");
                    foreach (string value in database.Keys)
                    {
                        sb.AppendLine(value);
                    }
                }
                else
                {
                    sb.AppendLine($"Detail for {listName}:");
                    foreach (ulong value in database[listName])
                    {
                        sb.AppendLine($"{value} = <#{value}>");
                    }
                }
                await command.RespondAsync(sb.ToString());
            }
            if (opt.Name == "add")
            {
                SocketSlashCommandDataOption optList = opt.Options.First<SocketSlashCommandDataOption>((x) => { return x.Name == "addselect"; });
                SocketSlashCommandDataOption optChannel = opt.Options.First<SocketSlashCommandDataOption>((x) => { return x.Name == "addchannel"; });
                string list = (string)optList.Value;
                SocketChannel sc = (SocketChannel)optChannel.Value;
                await command.RespondAsync($"Added {sc.Id} to {list}");
                Add(list, sc.Id);
            }
            if (opt.Name == "remove")
            {
                SocketSlashCommandDataOption optList = opt.Options.First<SocketSlashCommandDataOption>((x) => { return x.Name == "removeselect"; });
                SocketSlashCommandDataOption optChannel = opt.Options.First<SocketSlashCommandDataOption>((x) => { return x.Name == "removechannel"; });
                string list = (string)optList.Value;
                SocketChannel sc = (SocketChannel)optChannel.Value;
                await command.RespondAsync($"Removed {sc.Id} from {list}");
                Remove(list, sc.Id);
            }
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

        private void Log(LogSeverity severity, string text)
        {
            LogMessage logMessage = new LogMessage(severity, "Whitelist", text);
            Program.LogAsync(logMessage);
        }
    }
}

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.Text;
using System.Security.Cryptography;
using Salaros.Configuration;

namespace discord_bot
{
    internal static class Program
    {
        static string bot_token = "";
        static string seed = "";
        static ulong dvach_channel_id = 1171872312153362602;

        static string[] avatars;
        static string[] emojis;

        public static DiscordChannel? dvach_channel;
        public static DiscordWebhook? dvach_webhook = null;
        public static DiscordClient? discord = null;

        static async Task Main(string[] args)
        {
            string program_path = Directory.GetCurrentDirectory();
            string avatars_path = Path.Combine(program_path, "avatars.txt");
            avatars = File.ReadAllLines(avatars_path);
            string emojis_path = Path.Combine(program_path, "emojis.txt");
            emojis = File.ReadAllLines(emojis_path);

            ConfigParser iniparser = new ConfigParser(Path.Combine(program_path, "settings.ini"));
            bot_token = iniparser.GetValue("Settings", "token");
            seed = iniparser.GetValue("Settings", "seed");
            dvach_channel_id = ulong.Parse(iniparser.GetValue("Settings", "channel_id"));

            discord = new DiscordClient(new DiscordConfiguration()
            {
                Token = bot_token,
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents,
            });

            discord.MessageCreated += OnMessageSent;
            discord.ComponentInteractionCreated += OnComponentInterraction;

            dvach_channel = await discord.GetChannelAsync(dvach_channel_id);
            if(dvach_channel is null)
            {
                Console.WriteLine("Could not find dvach channel!");
                return;
            }

            var webhooks = await dvach_channel.GetWebhooksAsync();

            for (int i = 0; i < webhooks.Count; i++)
            {
                if (webhooks[i].Name == "dvach")
                {
                    dvach_webhook = webhooks[i];
                    break;
                }
            }
            if (dvach_webhook == null)
            {
                dvach_webhook = await dvach_channel.CreateWebhookAsync("dvach");
            }

            await discord.ConnectAsync();

            Console.WriteLine("Bot Started.");

            await Task.Delay(-1);
        }

        private static async Task OnMessageSent(DiscordClient sender, MessageCreateEventArgs args)
        {
            if (args.Author.IsBot)
                return;

            if (args.Message.Channel.IsPrivate)
            {
                await DmMessage(args);
                return;
            }

            else if (args.Message.Channel.Id == dvach_channel_id)
            {
                await DvachCommands(args);
                return;
            }
        }

        // комманды админа на дваче
        private static async Task DvachCommands(MessageCreateEventArgs args)
        {
            if (!args.Message.Content.StartsWith('/'))
                return;

            string[] tokens = args.Message.Content.Split(new char[] { ' ' });

            // БАН НАХУЙ
            if(tokens[0] == "/ban")
            {
                if (args.Message.ReferencedMessage != null)
                {
                    var webhook = new DiscordWebhookBuilder().WithContent(args.Message.ReferencedMessage.Content + "\n```diff\n-ПОЛЬЗОВАТЕЛЬ БЫЛ ЗАБАНЕН ЗА ЭТОТ ПОСТ-```");
                    await dvach_webhook.EditMessageAsync(args.Message.ReferencedMessage.Id, webhook, args.Message.ReferencedMessage.Attachments);
                    // потом надо над сохранением бана подумать
                }
            }

            await args.Message.DeleteAsync("deleted a command");
            return;
        }

        // личка бота
        private static async Task DmMessage(MessageCreateEventArgs args)
        {
            // replying to bot message
            if (args.Message.ReferencedMessage != null)
            {
                var ref_msg = args.Message.ReferencedMessage;
                if (!ref_msg.Author.IsBot || ref_msg.Embeds.Count < 1)
                    return;

                ulong reply_id = ulong.Parse(ref_msg.Embeds[0].Footer.Text);

                await Send2chMsg(args.Message.Author.Username, args.Message.Content, (List<DiscordAttachment>)args.Message.Attachments, reply_id);
            }
            else // regular message, send to 2ch
            {
                await Send2chMsg(args.Message.Author.Username, args.Message.Content, (List<DiscordAttachment>)args.Message.Attachments);
            }
        }

        // отправка сообщения в двач
        private static async Task Send2chMsg(string username, string content, List<DiscordAttachment> attachments, ulong? reply_id = null)
        {
            uint hash = GenerateHash(username + seed);

            string name = (hash % 10000).ToString("D4");
            string avatar = avatars[(hash * 3) % (avatars.Length - 1)];
            string emoji = emojis[(hash * 4) % (emojis.Length - 1)];

            string text_message = content.Replace("@", "\\@");
            if (attachments.Count > 0)
            {
                text_message += '\n';
                int enumerator = 1;
                foreach (var attachment in attachments)
                {
                    //Console.WriteLine(attachment.MediaType);
                    string media = attachment.MediaType.Substring(0, attachment.MediaType.IndexOf('/'));
                    if (media == "video" || media == "image")
                    {
                        string url = attachment.Url.Substring(0, attachment.Url.IndexOf('?'));
                        text_message += $"[||{media}-{enumerator}||]({url}) ";
                        enumerator++;
                    }
                }
            }

            var myButton = new DiscordButtonComponent(ButtonStyle.Secondary, "reply_button", "Ответить.", false, new DiscordComponentEmoji("⤴"));

            if (reply_id != null)
            {
                string temp = text_message;
                DiscordMessage? reply_msg = await dvach_channel.GetMessageAsync((ulong)reply_id);
                if(reply_msg != null)
                {
                    text_message = $"***В ответ на сообщение от [{reply_msg.Author.Username}](<{reply_msg.JumpLink}>).***\n" + temp;
                }
                else
                {
                    Console.WriteLine("Could not find message to reply to");
                }
            }

            var webhook = new DiscordWebhookBuilder()//.AddMentions(message.Mentions)
                .WithAvatarUrl(avatar)
                .WithUsername(name + emoji)
                .WithContent(text_message)
                .AddComponents(myButton);

            await dvach_webhook.ExecuteAsync(webhook);
        }

        // кнопки
        private static async Task OnComponentInterraction(DiscordClient sender, ComponentInteractionCreateEventArgs args)
        {
            // ответ на сообщения
            if (args.Id == "reply_button")
            {
                await args.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

                var embed = new DiscordEmbedBuilder()
                    .WithAuthor(args.Message.Author.Username, null, args.Message.Author.AvatarUrl)
                    .WithDescription(args.Message.Content)
                    .WithFooter("" + args.Message.Id);

                var msg = new DiscordMessageBuilder()
                    .WithContent("Ответь на это сообщение чтобы ответить анону.")
                    .WithEmbed(embed);

                var member = await args.Guild.GetMemberAsync(args.User.Id);
                var dm_channel = await member.CreateDmChannelAsync();

                await msg.SendAsync(dm_channel);
            }
        }

        private static uint GenerateHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] data = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                uint hash = BitConverter.ToUInt32(data, 0);
                return hash;
            }
        }
    }
}

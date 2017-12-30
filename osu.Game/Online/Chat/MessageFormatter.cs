﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace osu.Game.Online.Chat
{
    public static class MessageFormatter
    {
        // [[Performance Points]] -> wiki:Performance Points (https://osu.ppy.sh/wiki/Performance_Points)
        private static readonly Regex wiki_regex = new Regex(@"\[\[([^\]]+)\]\]");

        // (test)[https://osu.ppy.sh/b/1234] -> test (https://osu.ppy.sh/b/1234)
        private static readonly Regex old_link_regex = new Regex(@"\(([^\)]*)\)\[([a-z]+://[^ ]+)\]");

        // [https://osu.ppy.sh/b/1234 Beatmap [Hard] (poop)] -> Beatmap [hard] (poop) (https://osu.ppy.sh/b/1234)
        private static readonly Regex new_link_regex = new Regex(@"\[([a-z]+://[^ ]+) ([^\[\]]*(((?<open>\[)[^\[\]]*)+((?<close-open>\])[^\[\]]*)+)*(?(open)(?!)))\]");

        // advanced, RFC-compatible regular expression that matches any possible URL, *but* allows certain invalid characters that are widely used
        // This is in the format (<required>, [optional]):
        //      http[s]://<domain>.<tld>[:port][/path][?query][#fragment]
        private static readonly Regex advanced_link_regex = new Regex(@"(?<paren>\([^)]*)?" +
                @"(?<link>https?:\/\/" +
                    @"(?<domain>(?:[a-z0-9]\.|[a-z0-9][a-z0-9-]*[a-z0-9]\.)*[a-z][a-z0-9-]*[a-z0-9]" + // domain, TLD
                    @"(?::\d+)?)" + // port
                        @"(?<path>(?:(?:\/+(?:[a-z0-9$_\.\+!\*\',;:\(\)@&~=-]|%[0-9a-f]{2})*)*" + // path
                        @"(?:\?(?:[a-z0-9$_\+!\*\',;:\(\)@&=\/~-]|%[0-9a-f]{2})*)?)?" + // query
                        @"(?:#(?:[a-z0-9$_\+!\*\',;:\(\)@&=\/~-]|%[0-9a-f]{2})*)?)?)", // fragment
                RegexOptions.IgnoreCase);

        // 00:00:000 (1,2,3) - test
        private static readonly Regex time_regex = new Regex(@"\d\d:\d\d:\d\d\d? [^-]*");

        // #osu
        private static readonly Regex channel_regex = new Regex(@"#[a-zA-Z]+[a-zA-Z0-9]+");

        // Unicode emojis
        private static readonly Regex emoji_regex = new Regex(@"(\uD83D[\uDC00-\uDE4F])");

        private static void handleMatches(Regex regex, string display, string link, MessageFormatterResult result, int startIndex = 0, LinkAction? linkActionOverride = null)
        {
            int captureOffset = 0;
            foreach (Match m in regex.Matches(result.Text, startIndex))
            {
                var index = m.Index - captureOffset;

                var displayText = string.Format(display,
                                                m.Groups[0],
                                                m.Groups.Count > 1 ? m.Groups[1].Value : "",
                                                m.Groups.Count > 2 ? m.Groups[2].Value : "").Trim();

                var linkText = string.Format(link,
                                                m.Groups[0],
                                                m.Groups.Count > 1 ? m.Groups[1].Value : "",
                                                m.Groups.Count > 2 ? m.Groups[2].Value : "").Trim();

                if (displayText.Length == 0 || linkText.Length == 0) continue;

                // Check for encapsulated links
                if (result.Links.Find(l => l.Index <= index && l.Index + l.Length >= index + m.Length || index <= l.Index && index + m.Length >= l.Index + l.Length) == null)
                {
                    result.Text = result.Text.Remove(index, m.Length).Insert(index, displayText);

                    //since we just changed the line display text, offset any already processed links.
                    result.Links.ForEach(l => l.Index -= l.Index > index ? m.Length - displayText.Length : 0);

                    var details = getLinkDetails(link);
                    result.Links.Add(new Link(linkText, index, displayText.Length, linkActionOverride ?? details.linkType, details.linkArgument));

                    //adjust the offset for processing the current matches group.
                    captureOffset += m.Length - displayText.Length;
                }
            }
        }

        private static void handleAdvanced(Regex regex, MessageFormatterResult result, int startIndex = 0)
        {
            foreach (Match m in regex.Matches(result.Text, startIndex))
            {
                var index = m.Index;
                var prefix = m.Groups["paren"].Value;
                var link = m.Groups["link"].Value;
                var indexLength = link.Length;

                if (!String.IsNullOrEmpty(prefix))
                {
                    index += prefix.Length;
                    if (link.EndsWith(")"))
                    {
                        indexLength = indexLength - 1;
                        link = link.Remove(link.Length - 1);
                    }
                }

                var details = getLinkDetails(link);
                result.Links.Add(new Link(link, index, indexLength, details.linkType, details.linkArgument));
            }
        }

        private static (LinkAction linkType, string linkArgument) getLinkDetails(string url)
        {
            var args = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            args[0] = args[0].TrimEnd(':');

            switch (args[0])
            {
                case "http":
                case "https":
                    // length > 3 since all these links need another argument to work
                    if (args.Length > 3 && (args[1] == "osu.ppy.sh" || args[1] == "new.ppy.sh"))
                    {
                        switch (args[2])
                        {
                            case "b":
                            case "beatmaps":
                                return (LinkAction.OpenBeatmap, args[3]);
                            case "s":
                            case "beatmapsets":
                            case "d":
                                return (LinkAction.External, args[3]);
                        }
                    }

                    return (LinkAction.External, null);
                case "osu":
                    // every internal link also needs some kind of argument
                    if (args.Length < 3)
                        return (LinkAction.External, null);

                    LinkAction linkType;
                    switch (args[1])
                    {
                        case "chan":
                            linkType = LinkAction.OpenChannel;
                            break;
                        case "edit":
                            linkType = LinkAction.OpenEditorTimestamp;
                            break;
                        case "b":
                            linkType = LinkAction.OpenBeatmap;
                            break;
                        case "s":
                        case "dl":
                            linkType = LinkAction.OpenBeatmapSet;
                            break;
                        case "spectate":
                            linkType = LinkAction.Spectate;
                            break;
                        default:
                            linkType = LinkAction.External;
                            break;
                    }

                    return (linkType, args[2]);
                case "osump":
                    return (LinkAction.JoinMultiplayerMatch, args[1]);
                default:
                    return (LinkAction.External, null);
            }
        }

        private static MessageFormatterResult format(string toFormat, int startIndex = 0, int space = 3)
        {
            var result = new MessageFormatterResult(toFormat);

            // handle the [link display] format
            handleMatches(new_link_regex, "{2}", "{1}", result, startIndex);

            // handle the ()[] link format
            handleMatches(old_link_regex, "{1}", "{2}", result, startIndex);

            // handle wiki links
            handleMatches(wiki_regex, "{1}", "https://osu.ppy.sh/wiki/{1}", result, startIndex);

            // handle bare links
            handleAdvanced(advanced_link_regex, result, startIndex);

            // handle editor times
            handleMatches(time_regex, "{0}", "osu://edit/{0}", result, startIndex, LinkAction.OpenEditorTimestamp);

            // handle channels
            handleMatches(channel_regex, "{0}", "osu://chan/{0}", result, startIndex, LinkAction.OpenChannel);

            var empty = "";
            while (space-- > 0)
                empty += "\0";

            handleMatches(emoji_regex, empty, "{0}", result, startIndex);

            return result;
        }

        public static Message FormatMessage(Message inputMessage)
        {
            var result = format(inputMessage.Content);

            inputMessage.Content = result.Text;

            // Sometimes, regex matches are not in order
            result.Links.Sort();
            inputMessage.Links = result.Links;
            return inputMessage;
        }

        public class MessageFormatterResult
        {
            public List<Link> Links = new List<Link>();
            public string Text;
            public string OriginalText;

            public MessageFormatterResult(string text)
            {
                OriginalText = Text = text;
            }
        }
    }

    public enum LinkAction
    {
        External,
        OpenBeatmap,
        OpenBeatmapSet,
        OpenChannel,
        OpenEditorTimestamp,
        JoinMultiplayerMatch,
        Spectate,
    }

    public class Link : IComparable<Link>
    {
        public string Url;
        public int Index;
        public int Length;
        public LinkAction Action;
        public string Argument;

        public Link(string url, int startIndex, int length, LinkAction action, string argument)
        {
            Url = url;
            Index = startIndex;
            Length = length;
            Action = action;
            Argument = argument;
        }

        public int CompareTo(Link otherLink) => Index > otherLink.Index ? 1 : -1;
    }
}

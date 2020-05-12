using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeeRemote.Core.Network.Http
{
    // Each new route is assigned a key from permutations of `KeyBase` ("123456") and is stored in
    // `_routes` dictionary. Router implementation builds a composite regex from all routes
    // patterns that looks like
    //    route_pattern1 | route_pattern2 | route_pattern3 | route_pattern4 | ...
    // where `route_patternN` is prefixed with it's key pattern that looks like
    //    ^(?<__c1__>1)(?<__c5__>2)(?<__c3__>3)(?<__c2__>4)(?<__c4__>5)(?<__c6__>6)
    // These key patterns always match `KeyBase` ("123456") but in different named captures, so in the
    // sample key pattern above when matched against "123456/local/path" the `__c1__` to `__c6__`
    // named captures will concatenate to "143526" for currently matched route key. The corresponding
    // entry in `_routes` has `GroupStart` to `GroupEnd` that are used to extract handler data
    // dictionary from the composite regex anonymous captures.
    internal class Router : IDisposable
    {
        private const string KeyBase = "123456";

        private static readonly Regex _routePattern = new Regex(
            @"(/(({(?<data>[^}/:]+)(:(?<type>[^}/]+))?}?)|(?<static>[^/]+))|\*)",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private readonly Dictionary<string, RouteEntry> _routes = new Dictionary<string, RouteEntry>();

        private string[] _groupNames = new string[32];
        private Regex _pathParser;

        private IEnumerator<IEnumerable<char>> _permEnum =
            GetPermutations(KeyBase.ToCharArray(), KeyBase.Length).GetEnumerator();

        public void Dispose()
        {
            _permEnum?.Dispose();
            _permEnum = null;
        }

        public void Add(string route, RouteAction handler)
        {
            // for each "{key:type}" check regex pattern in `type` and raise `ArgumentException` on failure
            _routePattern.Replace(route, m =>
            {
                if (string.IsNullOrEmpty(m.Groups["static"].Value) && !string.IsNullOrEmpty(m.Groups["data"].Value)
                                                                   && !string.IsNullOrEmpty(m.Groups["type"].Value))
                {
                    Regex.Match(string.Empty, m.Groups["type"].Value);
                }

                return null;
            });
            _permEnum.MoveNext();
            if (_permEnum.Current != null)
            {
                _routes.Add(string.Join(null, _permEnum.Current), new RouteEntry { Pattern = route, Handler = handler });
            }

            _pathParser = null;
        }

        public bool TryGetValue(string localPath, out RouteAction handler, out Dictionary<string, string> data)
        {
            handler = null;
            data = null;
            if (_pathParser == null)
            {
                _pathParser = RebuildParser();
            }

            var match = _pathParser.Match(KeyBase + localPath);
            if (!match.Success)
            {
                return false;
            }

            string routeKey = null;
            for (var idx = 1; idx <= KeyBase.Length; idx++)
            {
                routeKey += match.Groups[$"__c{idx}__"].Value;
            }

            if (routeKey == null)
            {
                return false;
            }

            var entry = _routes[routeKey];
            handler = entry.Handler;
            if (entry.GroupStart < entry.GroupEnd)
            {
                data = new Dictionary<string, string>();
            }

            if (data == null)
            {
                return false;
            }

            for (var groupIdx = entry.GroupStart; groupIdx < entry.GroupEnd; groupIdx++)
            {
                data[_groupNames[groupIdx]] = match.Groups[groupIdx].Value;
            }

            return match.Success;
        }

        private static IEnumerable<IEnumerable<T>> GetPermutations<T>(IEnumerable<T> list, int length)
        {
            if (length == 1)
            {
                return list.Select(t => new[] { t });
            }

            return GetPermutations(list, length - 1).SelectMany(
                t => list
                    .Where(o => !t.Contains(o)),
                (t1, t2) => t1.Concat(new[] { t2 }));
        }

        private Regex RebuildParser()
        {
            var rev = new string[KeyBase.Length];
            var sb = new StringBuilder();
            var groupIdx = 1;

            foreach (var key in _routes.Keys)
            {
                var entry = _routes[key];
                entry.GroupStart = groupIdx;
                var el = 1;
                foreach (var c in key)
                {
                    rev[c - '1'] = $"(?<__c{el++}__>{c})";
                }

                sb.AppendLine((sb.Length > 0 ? "|" : null) + "^" + string.Join(null, rev) +
                              _routePattern.Replace(entry.Pattern, m =>
                              {
                                  var str = m.Groups["static"].Value;
                                  if (!string.IsNullOrEmpty(str))
                                  {
                                      return $"/{Regex.Escape(str)}";
                                  }

                                  str = m.Groups["data"].Value;
                                  if (string.IsNullOrEmpty(str))
                                  {
                                      return Regex.Escape(m.Groups[0].Value);
                                  }

                                  if (groupIdx >= _groupNames.Length)
                                  {
                                      Array.Resize(ref _groupNames, _groupNames.Length * 2);
                                  }

                                  _groupNames[groupIdx++] = str;
                                  str = m.Groups["type"].Value;
                                  return $"/({(string.IsNullOrEmpty(str) ? "[^/]*" : str)})";
                              }));
                entry.GroupEnd = groupIdx;
            }

            return new Regex(
                sb.ToString(),
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        }

        private class RouteEntry
        {
            public string Pattern { get; set; }

            public int GroupStart { get; set; }

            public int GroupEnd { get; set; }

            public RouteAction Handler { get; set; }
        }
    }
}

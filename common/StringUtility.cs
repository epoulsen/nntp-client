﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="StringUtility.cs" company="Sean McElroy">
//   Copyright Sean McElroy, 2014.  All rights reserved.
// </copyright>
// <summary>
//   A utility class that provides helper methods for manipulating strings
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace mcnntp.common
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A utility class that provides helper methods for manipulating strings
    /// </summary>
    public static class StringUtility
    {
        /// <summary>
        /// Compresses a string using ZLIB compression (Unix-style GZIP compression)
        /// </summary>
        /// <param name="text">The text to compress</param>
        /// <returns>A ZLIB compressed byte array representation of <paramref name="text"/></returns>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="text"/> is null or an empty string</exception>
        /// <exception cref="EncoderFallbackException">Thrown when characters are provided in the <param name="text"> that cannot be represented by UTF-8</param></exception>
        /// <exception cref="ObjectDisposedException">Thrown when the source or destination stream are disposed at the time they are internally copied for compression.  Should never occur.</exception>
        /// <exception cref="NotSupportedException">Thrown when the source stream does not support reading or the destination stream does not support writing.  Should never occur.</exception>
        public static async Task<byte[]> GZipCompressAsync(this string text, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentNullException("text");

            var buffer = Encoding.UTF8.GetBytes(text);
            using (var ms = new MemoryStream(buffer))
            using (var gzs = new Ionic.Zlib.ZlibStream(ms, Ionic.Zlib.CompressionMode.Compress, true))
            using (var output = new MemoryStream())
            {
                // Buffer size is default for underlying Stream.CopyToAsync()
                await gzs.CopyToAsync(output, 81920, cancellationToken);
                var array = output.ToArray();
                return array;
            }
        }

        /// <summary>
        /// Un-compresses a ZLIB-compressed (Unix-style GZIP compression) byte array to the original source text
        /// </summary>
        /// <param name="buffer">The byte array that represents the compressed data to un-compress</param>
        /// <returns>The original UTF-8 string that was compressed</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="buffer"/> is null or an empty string</exception>
        /// <exception cref="DecoderFallbackException">Thrown when characters are provided in the <param name="buffer"> that cannot be represented by UTF-8</param></exception>
        /// <exception cref="ArgumentException">Thrown when a byte array is passed in for the <paramref name="buffer"/> contains invalid data for the decompression routine.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the source or destination stream are disposed at the time they are internally copied for compression.  Should never occur.</exception>
        /// <exception cref="NotSupportedException">Thrown when the source stream does not support reading or the destination stream does not support writing.  Should never occur.</exception>
        public static async Task<string> GZipUncompressAsync(this byte[] buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (buffer == null || buffer.LongLength == 0)
                throw new ArgumentNullException("buffer");

            using (var ms = new MemoryStream(buffer))
            using (var gzs = new Ionic.Zlib.ZlibStream(ms, Ionic.Zlib.CompressionMode.Decompress, true))
            using (var output = new MemoryStream())
            {
                // Buffer size is default for underlying Stream.CopyToAsync()
                await gzs.CopyToAsync(output, 81920, cancellationToken);
                var array = output.ToArray();
                var str = Encoding.UTF8.GetString(array);
                return str;
            }
        }

        // ReSharper disable once InconsistentNaming
        public static bool MatchesAsIPInCIDRRange(this string test, string cidrMask)
        {
            IPAddress address;
            return IPAddress.TryParse(test, out address) && address.MatchesCIDRRange(cidrMask);
        }

        public static bool MatchesWildchar(this string test, string mask)
        {
            return Regex.IsMatch(test, "^" + Regex.Escape(mask).Replace(@"\*", ".*").Replace(@"\?", ".") + "$");
        }

        /// <summary>
        /// Tests the supplied string <paramref name="test"/> against a 'wildmat' pattern
        /// to see if it matches.
        /// </summary>
        /// <param name="test">The string input to test the wildmat against</param>
        /// <param name="wildmat">The pattern to test against the input.  This pattern is in the format as defined in RFC 3397 4.2</param>
        /// <returns>True if the test input string matches the wildmat pattern.  Otherwise, false.</returns>
        /// <remarks>See <a href="http://tools.ietf.org/html/rfc3977#section-4.2">RFC 3977</a> for more information.</remarks>
        /// <exception cref="ArgumentNullException">Thrown when the supplied test string is null</exception>
        /// <exception cref="RegexMatchTimeoutException">Thrown when it takes longer than 10 seconds to test the input string against the pattern</exception>
        public static bool MatchesWildmat(this string test, string wildmat)
        {
            if (string.IsNullOrEmpty(test))
                throw new ArgumentNullException("test");
            if (string.IsNullOrEmpty(wildmat))
                return true;

            // RFC 3977 4.2 - Right most part that matches wins
            var wildmatPatterns = wildmat.Split(',').Reverse();
            foreach (var wildmatPattern in wildmatPatterns)
            {
                var negate = false;
                var wildmatPattern2 = wildmatPattern;
                if (wildmatPattern2.StartsWith("!"))
                {
                    negate = true;
                    try
                    {
                        wildmatPattern2 = wildmatPattern2.Substring(1);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return false;
                    }
                }

                try
                {
                    var regexPattern = "^" + Regex.Escape(wildmatPattern2).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                    if (Regex.IsMatch(test, regexPattern, RegexOptions.IgnoreCase, new TimeSpan(0, 0, 10)))
                        return !negate;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Searches through a block of text for delimiters that separate them into segments and provides an
        /// enumeration over each delimited segment
        /// </summary>
        /// <param name="block">The string block of text over which to enumerate segments</param>
        /// <param name="delimiter">The delimiter that separates each segment</param>
        /// <returns>An enumeration of the <paramref name="block"/> separated by each <see cref="delimiter"/>.  The delimiter is not returned as part of any segment</returns>
        /// <exception cref="ArgumentNullException">Thrown when the block of text or the delimiter is null or an empty string</exception>
        /// <exception cref="OverflowException">Thrown when the length of the delimiter is longer than Int32.MaxValue</exception>
        public static IEnumerable<string> SeekThroughDelimiters(this string block, string delimiter)
        {
            return block.ToCharArray().SeekThroughDelimiters(delimiter.ToCharArray()).Select(s => new string(s));
        }

        /// <summary>
        /// Searches through a an array for delimiters that separate them into segments and provides an
        /// enumeration over each delimited array segment
        /// </summary>
        /// <typeparam name="T">The type of the enumerable array within which to search for the delimiter sub-array</typeparam>
        /// <param name="block">The string block of text over which to enumerate segments</param>
        /// <param name="delimiter">The delimiter that separates each segment</param>
        /// <returns>An enumeration of the <paramref name="block"/> separated by each <see cref="delimiter"/>.  The delimiter is not returned as part of any segment</returns>
        /// <exception cref="ArgumentNullException">Thrown when the block of text or the delimiter is null or an empty string</exception>
        /// <exception cref="OverflowException">Thrown when the length of the delimiter array is longer than Int32.MaxValue</exception>
        public static IEnumerable<T[]> SeekThroughDelimiters<T>(this T[] block, T[] delimiter)
            where T : IComparable
        {
            var start = 0;
            var b = 0;

            while (b < block.LongLength)
            {
                var match = false;
                var bx = b;
                var d = 0;
                while (b < block.LongLength && d < delimiter.LongLength && block[b].Equals(delimiter[d]))
                {
                    match = true;
                    b++;
                    d++;
                }

                if (match && d == delimiter.LongLength)
                {
                    yield return block.Skip(start).Take(b - delimiter.Length - start).ToArray();
                    start = b;
                }
                else
                    b = bx + 1;
            }

            if (start < block.LongLength)
                yield return block.Skip(start).ToArray();
        }

        /// <summary>
        /// Attempts to parse a date in the value of a Date header field of a newsgroup article
        /// </summary>
        /// <param name="headerValue">The value of the Date header field</param>
        /// <param name="dateTime">The parsed <see cref="DateTime"/> value as parsed from the <paramref name="headerValue"/></param>
        /// <returns>A value indicating whether or not the parsing of the header value was successful</returns>
        public static bool TryParseNewsgroupDateHeader(this string headerValue, out DateTime dateTime)
        {
            dateTime = DateTime.MinValue;

            if (string.IsNullOrEmpty(headerValue))
                return false;

            try
            {
                if (DateTime.TryParseExact(
                    headerValue,
                    "dd MMM yyyy HH:mm:ss K",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out dateTime)) return true;

                if (DateTime.TryParseExact(
                    headerValue,
                    "dd MMM yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out dateTime)) return true;

                if (DateTime.TryParseExact(
                    headerValue,
                    "ddd, dd MMM yyyy HH:mm:ss K",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out dateTime)) return true;

                if (DateTime.TryParseExact(
                    headerValue,
                    "ddd, dd MMM yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out dateTime)) return true;
            }
            catch (ArgumentException)
            {
                return false;
            }

            return false;
        }

        public static IEnumerable<KeyValuePair<string, string>> GetHeaders(IEnumerable<string> lines)
        {
            if (lines == null)
                throw new InvalidOperationException("No lines are part of this response");

            var ret = new List<KeyValuePair<string, string>>();

            // Handle header unfolding
            string? key = null;
            var sbValue = new StringBuilder();
            foreach (var line in lines)
            {
                // Break between header and body
                if (string.IsNullOrEmpty(line))
                    yield break;

                if (!string.IsNullOrEmpty(line) && (line[0] == ' ' || line[0] == '\t'))
                {
                    // Continued content (RFC 3977 8.3.2 on unfolding)
                    sbValue.Append(line.Replace("\r\n", "").Replace("\t", " "));
                    continue;
                }

                // Next line is here.  Add last (unwrapped) line.
                if (key != null)
                    yield return new KeyValuePair<string, string>(key, sbValue.ToString());
                sbValue.Clear();
                key = line.Substring(0, line.IndexOf(':'));

                sbValue.Append(line.Substring(line.IndexOf(':') + 2));
            }

            // Yield the last one
            if (key != null)
                yield return new KeyValuePair<string, string>(key, sbValue.ToString());
        }
    }
}

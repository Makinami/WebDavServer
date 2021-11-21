﻿// <copyright file="RequestLogMiddleware.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;

namespace FubarDev.WebDavServer.AspNetCore.Logging
{
    /// <summary>
    /// The request log middleware.
    /// </summary>
    public class RequestLogMiddleware
    {
        private static readonly IEnumerable<MediaType> _xmlMediaTypes = new[]
        {
            "text/xml",
            "application/xml",
            "text/plain",
        }.Select(x => new MediaType(x)).ToList();

        private static readonly Encoding _defaultEncoding = new UTF8Encoding(false);
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLogMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestLogMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware.</param>
        /// <param name="logger">The logger for this middleware.</param>
        public RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Tests if the media type qualifies for XML deserialization.
        /// </summary>
        /// <param name="mediaType">The media type to test.</param>
        /// <returns><see langword="true"/> when the media type might be an XML type.</returns>
        public static bool IsXml(string mediaType)
        {
            var contentType = new MediaType(mediaType);
            var isXml = _xmlMediaTypes.Any(x => contentType.IsSubsetOf(x));
            return isXml;
        }

        /// <summary>
        /// Tests if the media type qualifies for XML deserialization.
        /// </summary>
        /// <param name="mediaType">The media type to test.</param>
        /// <returns><see langword="true"/> when the media type might be an XML type.</returns>
        public static bool IsXml(MediaType mediaType)
        {
            var isXml = _xmlMediaTypes.Any(mediaType.IsSubsetOf);
            return isXml;
        }

        /// <summary>
        /// Invoked by ASP.NET core.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The async task.</returns>
        // ReSharper disable once ConsiderUsingAsyncSuffix
        public async Task Invoke(HttpContext context)
        {
            using (_logger.BeginScope("RequestInfo"))
            {
                var info = new List<string>()
                {
                    $"{context.Request.Protocol} {context.Request.Method} {context.Request.GetDisplayUrl()}",
                };

                try
                {
                    info.AddRange(context.Request.Headers.Select(x => $"{x.Key}: {x.Value}"));
                }
                catch
                {
                    // Ignore all exceptions
                }

                var shouldTryReadingBody =
                    IsXmlContentType(context.Request)
                    || (context.Request.Body != null
                        && (IsMicrosoftWebDavClient(context.Request)
                            || IsLoggableMethod(context.Request)));

                if (shouldTryReadingBody && context.Request.Body != null)
                {
                    context.Request.EnableBuffering();

                    var encoding = GetEncoding(context.Request);
                    bool showRawBody;
                    if (HttpMethods.IsPut(context.Request.Method))
                    {
                        showRawBody = true;
                    }
                    else
                    {
                        try
                        {
                            var temp = new byte[1];
                            var readCount = await context.Request.Body.ReadAsync(temp, context.RequestAborted);
                            if (readCount != 0)
                            {
                                context.Request.Body.Position = 0;

                                using (var reader = new StreamReader(context.Request.Body, encoding, false, 1000, true))
                                {
                                    var doc = await XDocument.LoadAsync(
                                            reader,
                                            LoadOptions.PreserveWhitespace,
                                            context.RequestAborted)
                                        .ConfigureAwait(false);
                                    info.Add($"Body: {doc}");
                                }
                            }

                            showRawBody = false;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(EventIds.Unspecified, ex, "Failed to read the request body as XML");
                            showRawBody = true;
                        }
                        finally
                        {
                            context.Request.Body.Position = 0;
                        }
                    }

                    if (showRawBody)
                    {
                        try
                        {
                            using var reader = new StreamReader(context.Request.Body, encoding, false, 1000, true);
                            var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(content))
                            {
                                info.Add($"Body: {content}");
                            }
                        }
                        finally
                        {
                            context.Request.Body.Position = 0;
                        }
                    }
                }

                _logger.LogInformation("Request information: {Information}", string.Join("\r\n", info));
            }

            await _next(context).ConfigureAwait(false);
        }

        private static bool IsLoggableMethod(HttpRequest request)
        {
            return request.Method switch
            {
                "PROPPATCH" => true,
                "PROPFIND" => true,
                "LOCK" => true,
                _ => false,
            };
        }

        private static bool IsXmlContentType(HttpRequest request)
        {
            return request.Body != null
                   && !string.IsNullOrEmpty(request.ContentType)
                   && IsXml(request.ContentType);
        }

        private static bool IsMicrosoftWebDavClient(HttpRequest request)
        {
            if (!request.Headers.TryGetValue("User-Agent", out var userAgentValues))
            {
                return false;
            }

            if (userAgentValues.Count == 0)
            {
                return false;
            }

            return userAgentValues[0].IndexOf("Microsoft-WebDAV-MiniRedir", StringComparison.OrdinalIgnoreCase) != -1;
        }

        private static Encoding GetEncoding(HttpRequest request)
        {
            if (string.IsNullOrEmpty(request.ContentType))
            {
                return _defaultEncoding;
            }

            var contentType = new MediaType(request.ContentType);
            if (contentType.Charset.HasValue)
            {
                return Encoding.GetEncoding(contentType.Charset.Value);
            }

            return _defaultEncoding;
        }
    }
}

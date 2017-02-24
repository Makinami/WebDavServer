﻿// <copyright file="EntityTagConverter.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Xml.Linq;

using FubarDev.WebDavServer.Model.Headers;

namespace FubarDev.WebDavServer.Props.Converters
{
    public class EntityTagConverter : IPropertyConverter<EntityTag>
    {
        /// <inheritdoc />
        public EntityTag FromElement(XElement element)
        {
            return EntityTag.FromXml(element);
        }

        /// <inheritdoc />
        public XElement ToElement(XName name, EntityTag value)
        {
            return value.ToXml();
        }
    }
}

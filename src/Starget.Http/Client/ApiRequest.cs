﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Starget.Http.Client
{
    public class ApiRequest
    {
        public string Url { get; protected set; }
        public RequestType RequestType { get; protected set; }
        public Dictionary<string, string> QueryStrings { get; protected set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Headers { get; protected set; } = new Dictionary<string, string>();
        public StringBuilder JsonBuilder { get; set; } = new StringBuilder();
        public Dictionary<string,string> Forms { get; protected set; } = new Dictionary<string, string>();
        public List<object> Objects { get; protected set; } = new List<object>();
        public JsonSerializerSettings JsonSerializerSettings { get; set; }
        public List<FileContent> Files { get; protected set; } = new List<FileContent>();
        public Func<object, HttpContent> SerializeObjectCallBack { get; set; } = null;

        public static ApiRequest FromModel<T>(T model,RequestType requestType,string url = null) where T : class
        {
            if (model == null)
            {
                return new ApiRequest();
            }

            var request = new ApiRequest();
            request.ParseModel(model, requestType, url);
            return request;
        }

        public ApiRequest() :this(null)
        {}

        public ApiRequest(string url)
        {
            this.Url = url;
        }

        public string GetJson()
        {
            var json = JsonConvert.SerializeObject(this.Forms);
            return json;
        }

        public void ParseModel(object model, RequestType requestType, string url = null)
        {
            if (model == null)
            {
                return;
            }

            this.RequestType = requestType;

            if (string.IsNullOrEmpty(url) == false)
            {
                this.Url = url;
            }

            var type = model.GetType();
            var defaultAttrType = ApiSerializeType.None;
            var attrs = type.GetCustomAttributes(typeof(ApiQueryAttribute), true);
            defaultAttrType = attrs != null && attrs.Count() > 0 ? ApiSerializeType.FromQuery : defaultAttrType;

            if (defaultAttrType == ApiSerializeType.None)
            {
                attrs = type.GetCustomAttributes(typeof(ApiHeaderAttribute), true);
                defaultAttrType = attrs != null && attrs.Count() > 0 ? ApiSerializeType.FromHeader : defaultAttrType;
            }

            if (defaultAttrType == ApiSerializeType.None)
            {
                attrs = type.GetCustomAttributes(typeof(ApiFormAttribute), true);
                defaultAttrType = attrs != null && attrs.Count() > 0 ? ApiSerializeType.FromForm : defaultAttrType;
            }

            if (defaultAttrType == ApiSerializeType.None)
            {
                defaultAttrType = this.RequestType == RequestType.Get ? ApiSerializeType.FromQuery : ApiSerializeType.FromForm;
            }

            var defaultTextCaseType = ApiSerializeTextCaseType.None;
            if (defaultTextCaseType == 0)
            {
                attrs = type.GetCustomAttributes(typeof(ApiCamelCaseAttribute), true);
                defaultTextCaseType = attrs != null && attrs.Count() > 0 ? ApiSerializeTextCaseType.CamelCase : defaultTextCaseType;
            }

            StringWriter sw = new StringWriter(this.JsonBuilder);
            var jw = new JsonTextWriter(sw);
            jw.WriteStartObject();
            var properties = type.GetProperties();
            foreach (var p in properties)
            {
                var attrType = ApiSerializeType.None;
                var textCaseType = ApiSerializeTextCaseType.None;
                attrs = p.GetCustomAttributes(typeof(ApiIgnoreAttribute), true);
                if (attrs != null && attrs.Count() > 0)
                {
                    continue;
                }

                var value = p.GetValue(model);

                attrs = p.GetCustomAttributes(typeof(ApiCamelCaseAttribute), true);
                textCaseType = attrs != null && attrs.Count() > 0 ? ApiSerializeTextCaseType.CamelCase : textCaseType;
                textCaseType = textCaseType > 0 ? textCaseType : defaultTextCaseType;

                var name = p.Name;
                if (textCaseType == ApiSerializeTextCaseType.CamelCase)
                {
                    name = name.ToCamelCase();
                }

                if (p.PropertyType.IsAssignableFrom(typeof(ISerializable)))
                {
                    var request = new ApiRequest();
                    request.ParseModel(value, this.RequestType, this.Url);

                    this.Url = this.Url ?? request.Url;

                    foreach (var key in request.QueryStrings.Keys)
                    {
                        this.QueryStrings[key] = request.QueryStrings[key];
                    }

                    foreach (var key in request.Headers.Keys)
                    {
                        this.Headers[key] = request.Headers[key];
                    }

                    var childJson = request.JsonBuilder.ToString();
                    if(string.IsNullOrEmpty(childJson) == false)
                    {
                        jw.WritePropertyName(name);
                        jw.WriteRawValue(childJson);
                    }
                    return;
                }

                if (this.Url == null)
                {
                    attrs = p.GetCustomAttributes(typeof(ApiUrlAttribute), true);
                    if (attrs != null && attrs.Count() > 0)
                    {
                        this.Url = Convert.ToString(p.GetValue(model));
                        continue;
                    }
                }

                attrs = p.GetCustomAttributes(typeof(ApiQueryAttribute), true);
                attrType = attrs != null && attrs.Count() > 0 ? ApiSerializeType.FromQuery : attrType;

                if (attrType == ApiSerializeType.None)
                {
                    attrs = type.GetCustomAttributes(typeof(ApiHeaderAttribute), true);
                    attrType = attrs != null && attrs.Count() > 0 ? ApiSerializeType.FromHeader : attrType;
                }

                if (attrType == ApiSerializeType.None)
                {
                    attrs = type.GetCustomAttributes(typeof(ApiFormAttribute), true);
                    attrType = attrs != null && attrs.Count() > 0 ? ApiSerializeType.FromForm : attrType;
                }

                attrType = attrType > 0 ? attrType : defaultAttrType;

                if (attrType == ApiSerializeType.FromQuery)
                {
                    this.AddQueryParameter(name, Convert.ToString(p.GetValue(model)));
                }
                else if (attrType == ApiSerializeType.FromHeader)
                {
                    this.AddHeaderParameter(name, Convert.ToString(p.GetValue(model)));
                }

                else if (attrType == ApiSerializeType.FromForm)
                {
                    attrs = p.GetCustomAttributes(typeof(ApiFileAttribute), true);
                    if (attrs != null && attrs.Count() > 0)
                    {
                        var attr = attrs[0] as ApiFileAttribute;
                        name = attr.Name;
                        var fileName = attr.FileName;
                        if (string.IsNullOrEmpty(attr.FileNameProperty) == false)
                        {
                            var fileNameProperty = type.GetProperty(attr.FileNameProperty);
                            var fileNameObj = fileNameProperty.GetValue(model);
                            if (fileNameObj != null)
                            {
                                var str = Convert.ToString(fileNameObj);
                                name = name ?? str;
                                fileName = fileName ?? str;
                            }
                        }
                        if (p.PropertyType.IsAssignableFrom(typeof(byte[])))
                        {
                            var bytes = value as byte[];
                            this.AddFile(name, fileName, bytes);
                        }
                        else if (p.PropertyType.IsAssignableFrom(typeof(Stream)))
                        {
                            var stream = value as Stream;
                            this.AddFile(name, fileName, stream);
                        }
                        else if (p.PropertyType.IsAssignableFrom(typeof(string)) || p.PropertyType.IsAssignableFrom(typeof(String)))
                        {
                            var filePath = value as string;
                            this.AddFile(name, fileName, filePath);
                        }
                    }
                    else
                    {
                        jw.WritePropertyName(name);
                        jw.WriteValue(value);
                    }
                }
            }

            jw.WriteEndObject();

        }

        public void AddQueryParameter(string key,string value)
        {
            this.QueryStrings[key] = value;
        }

        public void RemoveQueryParameter(string key)
        {
            this.QueryStrings.Remove(key);
        }

        public string GetQueryParameter(string key)
        {
            if(this.QueryStrings.ContainsKey(key) == false)
            {
                return null;
            }
            return this.QueryStrings[key];
        }

        public void AddHeaderParameter(string key, string value)
        {
            this.Headers[key] = value;
        }

        public void RemoveHeaderParameter(string key)
        {
            this.Headers.Remove(key);
        }

        public string GetHeaderParameter(string key)
        {
            if (this.Headers.ContainsKey(key) == false)
            {
                return null;
            }
            return this.Headers[key];
        }

        public void AddObject<T>(T obj) where T : ISerializable
        {
            this.Objects.Add(obj);
        }

        public bool RemoveObject<T>(T obj) where T : ISerializable
        {
            return this.Objects.Remove(obj);
        }

        public void AddFile(string name,string fileName, string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            AddFile(name, fileName, bytes);
        }

        public void AddFile(string name,string fileName,Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                var bytes = ms.ToArray();
                AddFile(name, fileName, bytes);
            }
        }

        public void RemoveFile(string name)
        {
            this.Files.RemoveAll(el => el.Name == name);
        }

        public void AddFile(string name, string fileName, byte[] bytes)
        {
            FileContent content = new FileContent();
            content.Name = name;
            content.FileName = fileName;
            content.Bytes = bytes;
            this.Files.Add(content);
        }

        public string GetQueryString()
        {
            if(this.QueryStrings.Keys.Count == 0)
            {
                return "";
            }
            string str = "";
            foreach(var key in this.QueryStrings.Keys)
            {
                str += string.Format("{0}={1}&", key, HttpUtility.UrlEncode(this.QueryStrings[key]));
            }
            str = str.TrimEnd('&');
            return str;
        }

        public string GetUrl()
        {
            string url = this.Url;
            string query = GetQueryString();
            if(string.IsNullOrEmpty(query) == false)
            {
                url = url + "?" + query;
            }
            return url;
        }

        public virtual HttpRequestMessage GetRequestMessage()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.RequestUri = new Uri(this.GetUrl(),UriKind.RelativeOrAbsolute);
            foreach(var key in this.Headers.Keys)
            {
                request.Headers.Add(key, this.Headers[key]);
            }

            List<HttpContent> contents = new List<HttpContent>();
            var jsonBody = this.JsonBuilder.ToString();
            if (string.IsNullOrEmpty(jsonBody) == false)
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                contents.Add(content);
            }
            if(this.Objects.Count > 0)
            {
                foreach(var obj in this.Objects)
                {
                    if(this.SerializeObjectCallBack != null)
                    {
                        var content = this.SerializeObjectCallBack(obj);
                        contents.Add(content);
                    }
                    else
                    {
                        var json = JsonConvert.SerializeObject(obj, this.JsonSerializerSettings);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        contents.Add(content);
                    }
                }
            }

            if(contents.Count == 1)
            {
                request.Content = contents[0];
            }
            else if(contents.Count > 1 || this.Files.Count > 0)
            {
                var multipart = new MultipartFormDataContent();
                foreach(var c in contents)
                {
                    multipart.Add(c);
                }

                if (this.Files.Count() > 0)
                {
                    foreach (var file in this.Files)
                    {
                        ByteArrayContent fileContent = new ByteArrayContent(file.Bytes);
                        multipart.Add(fileContent,file.Name,file.FileName);
                    }
                }
                request.Content = multipart;
            }
            return request;
        }


    }
}

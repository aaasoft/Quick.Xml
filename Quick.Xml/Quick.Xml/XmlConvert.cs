using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Quick.Xml
{
    public class XmlConvert
    {
        /*
&lt;    <   小于号
&gt;    >   大于号
&amp;   &   和
&apos;  ’   单引号
&quot;  "   双引号
            */

        private static Dictionary<string, string> escapeDict = new Dictionary<string, string>()
        {
            ["&"] = "&amp;",
            ["<"] = "&lt;",
            [">"] = "&gt;",
            ["’"] = "&apos;",
            ["\""] = "&quot;"
        };

        private static Dictionary<string, string> unescapeDict = new Dictionary<string, string>()
        {
            ["&lt;"] = "<",
            ["&gt;"] = ">",
            ["&apos;"] = "’",
            ["&quot;"] = "\"",
            ["&amp;"] = "&"
        };

        /// <summary>
        /// XML转义
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        public static string Escape(string xml)
        {
            StringBuilder sb = new StringBuilder(xml);
            foreach (var key in escapeDict.Keys)
                sb.Replace(key, escapeDict[key]);
            return sb.ToString();
        }

        /// <summary>
        /// XML反向转义
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string Unescape(string text)
        {
            StringBuilder sb = new StringBuilder(text);
            foreach (var key in unescapeDict.Keys)
                sb.Replace(key, unescapeDict[key]);
            return sb.ToString();
        }
        private static object CreateInstance(Type type, XmlConvertOptions options)
        {
            if (options?.InstanceFactory != null)
            {
                if (options.InstanceFactory.TryGetValue(type, out var factory))
                    return factory.Invoke();
            }
            return Activator.CreateInstance(type);
        }

        public static T Deserialize<T>(string xml, XmlConvertOptions options = null)
            where T : class
        {
            return Deserialize(xml, options) as T;
        }

        public static object Deserialize(string xml,XmlConvertOptions options = null)
        {
            XDocument document = XDocument.Parse(xml);
            var rootElement = document.Root;
            var rootElementType = getXmlNameType(rootElement.Name);
            if (rootElementType == null)
                return null;
            //准备字典
            Dictionary<PropertyInfo, JsonIgnoreAttribute> dictPropertyInfo_JsonIgnoreAttribute = new Dictionary<PropertyInfo, JsonIgnoreAttribute>();

            var rootElementObj = CreateInstance(rootElementType, options);
            foreach (var pi in rootElementType.GetProperties())
                ReadProperty(rootElementObj, pi, rootElement, dictPropertyInfo_JsonIgnoreAttribute, options);
            return rootElementObj;
        }

        public static void ReadProperty(object obj, PropertyInfo pi, XElement element, IDictionary<PropertyInfo, JsonIgnoreAttribute> dictPropertyInfo_JsonIgnoreAttribute, XmlConvertOptions options)
        {
            JsonIgnoreAttribute jsonIgnoreAttribute = null;
            if (!dictPropertyInfo_JsonIgnoreAttribute.ContainsKey(pi))
            {
#if NETSTANDARD2_0 || NET45
                jsonIgnoreAttribute = pi.GetCustomAttribute<JsonIgnoreAttribute>();
#else
                jsonIgnoreAttribute = pi.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).FirstOrDefault() as JsonIgnoreAttribute;
#endif
                dictPropertyInfo_JsonIgnoreAttribute[pi] = jsonIgnoreAttribute;
            }
            jsonIgnoreAttribute = dictPropertyInfo_JsonIgnoreAttribute[pi];
            //如果属性不能读取
            //或者配置了JsonIgnoreAttribute特性
            if (jsonIgnoreAttribute != null
                || !pi.CanRead
                || !pi.CanWrite
                || pi.GetGetMethod().IsStatic
                || pi.GetSetMethod().IsStatic)
                return;
            //如果是列表
            if (typeof(System.Collections.IList).IsAssignableFrom(pi.PropertyType))
            {
                System.Collections.IList list = null;

                if (pi.PropertyType.IsInterface
                    || pi.PropertyType.IsAbstract
                    || pi.PropertyType.IsArray)
                    list = new System.Collections.ArrayList();
                else
                    list = (System.Collections.IList)CreateInstance(pi.PropertyType, options);

                var piElement = element.Element(pi.Name);
                if (piElement == null)
                    return;
                foreach (var itemElement in piElement.Elements())
                {
                    var itemType = getXmlNameType(itemElement.Name);
                    if (itemType == null)
                    {
                        options?.LogHandler?.Invoke($"警告：类型[{itemElement.Name}]未找到。");
                        continue;
                    }
                    var itemObject = CreateInstance(itemType, options);
                    foreach (var piItem in itemType.GetProperties())
                        ReadProperty(itemObject, piItem, itemElement, dictPropertyInfo_JsonIgnoreAttribute, options);
                    list.Add(itemObject);
                }
                if (pi.PropertyType.IsArray)
                {
                    var array = Array.CreateInstance(pi.PropertyType.GetElementType(), list.Count);
                    for (var i = 0; i < list.Count; i++)
                        array.SetValue(list[i], i);
                    pi.SetValue(obj, array, null);
                }
                else
                    pi.SetValue(obj, list, null);
                return;
            }
            //如果在属性里面能找到
            var attr = element.Attribute(pi.Name);
            if (attr != null)
            {
                var value = attr.Value;

                object valueObj = null;
                if (pi.PropertyType.IsEnum)
                    try { valueObj = Enum.Parse(pi.PropertyType, value); } catch { }
                else
                    valueObj = new JValue(value).ToObject(pi.PropertyType);
#if NETSTANDARD2_0 || NET45
                pi.SetValue(obj, valueObj);
#else
                pi.SetValue(obj, valueObj, null);
#endif
                return;
            }
            var propertyElement = element.Element(pi.Name);
            if (propertyElement == null)
                return;

            XElement tempElement = propertyElement;
            if (pi.PropertyType.IsInterface || pi.PropertyType.IsAbstract)
                tempElement = propertyElement.Elements().FirstOrDefault();

            var propertyValueType = getXmlNameType(tempElement.Name);
            if (propertyValueType == null)
                propertyValueType = pi.PropertyType;
            //如果是接口或者抽象类
            if (propertyValueType.IsInterface || propertyValueType.IsAbstract)
                return;
            var propertyValueObj = CreateInstance(propertyValueType, options);
            foreach (var piPropertyValue in propertyValueType.GetProperties())
            {
                var piName = piPropertyValue.Name;
                ReadProperty(propertyValueObj, piPropertyValue, tempElement, dictPropertyInfo_JsonIgnoreAttribute, options);
            }
#if NETSTANDARD2_0 || NET45
            pi.SetValue(obj, propertyValueObj);
#else
            pi.SetValue(obj, propertyValueObj, null);
#endif
        }

        private static Type getTypeFromClrNamespace(string clr_namespace, string typeName)
        {
            //App.Core.ViewModel;assembly=App.Core
            var strs = clr_namespace.Split(';');
            var ns = strs[0].Trim();
            var assembly = strs[1].Split('=')[1].Trim();
            var clazz = $"{ns}.{typeName}";
            return Type.GetType($"{clazz},{assembly}");
        }

        private static Type getXmlNameType(XName name)
        {
            if (name.Namespace == null || name.Namespace == XNamespace.None)
                return null;
            var namespaceName = name.NamespaceName;
            Uri uri = new Uri(namespaceName);
            switch (uri.Scheme)
            {
                case "clr-namespace":
                    return getTypeFromClrNamespace(uri.AbsolutePath, name.LocalName);
            }
            return null;
        }

        /// <summary>
        /// 将对象序列化为XML
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="rootElementName"></param>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public static string Serialize(object obj, string rootElementName = null)
        {
            if (obj == null)
                return null;
            var type = obj.GetType();
            if (rootElementName == null)
                rootElementName = type.Name;

            //准备字典
            Dictionary<PropertyInfo, JsonIgnoreAttribute> dictPropertyInfo_JsonIgnoreAttribute = new Dictionary<PropertyInfo, JsonIgnoreAttribute>();
            Dictionary<XNamespace, string> dictNamespace = new Dictionary<XNamespace, string>();

            var projectNs = getTypeXmlNamespace(obj.GetType(), dictNamespace);
            var rootElement = new XElement(projectNs + rootElementName);

            foreach (var pi in type.GetProperties())
                WriteProperty(obj, pi, rootElement, dictNamespace, dictPropertyInfo_JsonIgnoreAttribute);

            foreach (var ns in dictNamespace.Keys)
            {
                var shortName = dictNamespace[ns];
                if (string.IsNullOrEmpty(shortName))
                    rootElement.Add(new XAttribute("xmlns", ns.NamespaceName));
                else
                    rootElement.Add(new XAttribute(XNamespace.Xmlns + shortName, ns));
            }
            var encoding = new UTF8Encoding(false);

            //构建XML
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                rootElement
            );
            //输出XML
            using (var stream = new MemoryStream())
            using (var writer = new XmlTextWriter(stream, encoding))
            {
                writer.Formatting = System.Xml.Formatting.Indented;
                document.Save(writer);
                writer.Close();
                return encoding.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// 获取类型的命名空间
        /// </summary>
        /// <param name="type"></param>
        /// <param name="dictNamespace"></param>
        /// <param name="isDefaultNs"></param>
        /// <returns></returns>
        private static XNamespace getTypeXmlNamespace(Type type, IDictionary<XNamespace, string> dictNamespace = null)
        {
            var assemblyName = type.Assembly.GetName().Name;
            var key = XNamespace.Get($"clr-namespace:{type.Namespace};assembly={assemblyName}");

            if (dictNamespace == null || dictNamespace.ContainsKey(key))
                return key;

            string prefix = type.Namespace.Split('.').LastOrDefault();
            var shortName = prefix;
            int index = 1;
            while (dictNamespace.Any(t => t.Value == shortName))
            {
                shortName = $"{prefix}{index}";
                index++;
            }
            dictNamespace[key] = shortName;
            return key;
        }

        /// <summary>
        /// 将属性写入XElement对象
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="pi"></param>
        /// <param name="parentElement"></param>
        /// <param name="dictNamespace"></param>
        /// <param name="dictPropertyInfo_JsonIgnoreAttribute"></param>
        public static void WriteProperty(object obj, PropertyInfo pi, XElement parentElement, IDictionary<XNamespace, string> dictNamespace, IDictionary<PropertyInfo, JsonIgnoreAttribute> dictPropertyInfo_JsonIgnoreAttribute)
        {
            JsonIgnoreAttribute jsonIgnoreAttribute = null;
            if (!dictPropertyInfo_JsonIgnoreAttribute.ContainsKey(pi))
            {
#if NETSTANDARD2_0 || NET45
                jsonIgnoreAttribute = pi.GetCustomAttribute<JsonIgnoreAttribute>();
#else
                jsonIgnoreAttribute = pi.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).FirstOrDefault() as JsonIgnoreAttribute;
#endif
                dictPropertyInfo_JsonIgnoreAttribute[pi] = jsonIgnoreAttribute;
            }
            jsonIgnoreAttribute = dictPropertyInfo_JsonIgnoreAttribute[pi];
            //如果属性不能读取
            //或者配置了JsonIgnoreAttribute特性
            if (!pi.CanRead
                || jsonIgnoreAttribute != null
                || !pi.CanRead
                || !pi.CanWrite
                || pi.GetGetMethod().IsStatic
                || pi.GetSetMethod().IsStatic)
                return;
#if NETSTANDARD2_0 || NET45
            var value = pi.GetValue(obj);
#else
            var value = pi.GetValue(obj, null);
#endif
            if (value == null)
                return;
            var valueType = value.GetType();
            //如果是列表
            if (typeof(System.Collections.IList).IsAssignableFrom(valueType))
            {
                var element = new XElement(pi.Name);
                var list = value as System.Collections.IList;
                foreach (var item in list)
                {
                    var itemType = item.GetType();
                    var ns = getTypeXmlNamespace(itemType, dictNamespace);
                    var itemElement = new XElement(ns + itemType.Name);
                    foreach (var pi2 in itemType.GetProperties())
                        WriteProperty(item, pi2, itemElement, dictNamespace, dictPropertyInfo_JsonIgnoreAttribute);
                    element.Add(itemElement);
                }
                parentElement.Add(element);
                return;
            }
            //如果是系统类型或者是枚举类型
            if (valueType.Namespace.StartsWith(nameof(System))
                || valueType.IsEnum)
            {
                parentElement.SetAttributeValue(pi.Name, value);
            }
            else
            {
                XElement element = null;
                if (valueType == pi.PropertyType)
                {
                    element = new XElement(pi.Name);
                    parentElement.Add(element);
                }
                else
                {
                    var ns = getTypeXmlNamespace(valueType, dictNamespace);
                    element = new XElement(ns + valueType.Name);
                    var element_parent = new XElement(pi.Name, element);
                    parentElement.Add(element_parent);
                }
                foreach (var pi2 in valueType.GetProperties())
                    WriteProperty(value, pi2, element, dictNamespace, dictPropertyInfo_JsonIgnoreAttribute);
            }
        }
    }
}

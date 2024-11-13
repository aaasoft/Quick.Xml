using System;
using System.Collections.Generic;
using System.Text;

namespace Quick.Xml
{
    public class XmlConvertOptions
    {
        public Dictionary<Type, Func<object>> InstanceFactory { get; set; }
        public Action<string> LogHandler { get; set; }
    }
}

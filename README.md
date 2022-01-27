# Quick.Xml 
[![NuGet Version](http://img.shields.io/nuget/v/Quick.Xml.svg?style=flat)](https://www.nuget.org/packages/Quick.Xml/)

Xml Serializer For .NET.

Example:
```csharp
using Quick.Xml

var xml = XmlConvert.Serialize(obj);
var obj = XmlConvert.Deserialize(xml);
```

Output:
```xml
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<Test:ClassA Name="I'm ClassA." xmlns:Test="clr-namespace:Quick.Xml.Test;assembl
y=Quick.Xml.Test">
  <BArray>
    <Test:ClassB Name="I'm ClassB." />
    <Test:ClassB Name="I'm ClassB too." />
  </BArray>
</Test:ClassA>
```

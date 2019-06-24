using System;

namespace Quick.Xml.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var model = new ClassA()
            {
                Name = "I'm ClassA.",
                BArray = new[]
                 {
                     new ClassB(){Name = "I'm ClassB."},
                     new ClassB(){Name = "I'm ClassB too."}
                 }
            };
            var xml = XmlConvert.Serialize(model);
            Console.WriteLine(xml);

            var model2 = XmlConvert.Deserialize(xml);
            Console.WriteLine("----------------");
            Console.WriteLine(model2);
            Console.ReadLine();
        }
    }
}

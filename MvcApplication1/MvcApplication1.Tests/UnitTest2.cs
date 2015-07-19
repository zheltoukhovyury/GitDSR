using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RabbitMQ.Client;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json.Linq;
using System.Text;
using RabbitMQ.Client.Events;
using MvcApplication1.App_Data;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace MvcApplication1.Tests
{
    [TestClass]
    public class UnitTest2
    {
        [TestMethod]
        public void OnNewCommandTest()
        {
            DataContextRealiztion context = new DataContextRealiztion();
            String devId = "deviceId";

            JObject command_1 = new JObject();
            command_1.Add("deviceId", devId);
            context.NewCommand(command_1);

            JObject command_2 = context.GetCommand(devId);


            // для JObject не срабатывает Equals хотя срабатывает для Jtoken
            BsonDocument doc1 = BsonDocument.Parse(command_1.ToString());
            BsonDocument doc2 = BsonDocument.Parse(command_2.ToString());

            bool test = doc1.Equals(doc2);

            Assert.IsTrue(doc1.Equals(doc2));
        }
    }
}

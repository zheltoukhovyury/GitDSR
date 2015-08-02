using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RabbitMQ.Client;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json.Linq;
using System.Text;
using RabbitMQ.Client.Events;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using MvcApplication1.Controllers;
using System.Threading;
using System.Configuration;
using Ninject;
using Moq;
using System.Web.Routing;
using MvcApplication1.Models;
using MvcApplication1.Context;

namespace MvcApplication1.Tests
{
    [TestClass]
    public class UnitTest2
    {
        [TestMethod]
        public void OnNewCommandTest()
        {
            //тест не будет иметь доступ к к конфигу, поэтому аогументы хардкодом
            //на самом деле я б не стал делать такой тест. но больше и проверить-то нечего

            Context.RabbitMqContext context = Context.RabbitMqContext.RabbitMqContextFactory.GetContext();

            String devId = "deviceId";
            JObject command_1 = new JObject();
            command_1.Add("deviceId", devId);
            context.NewCommand(command_1);
            JObject command_2 = context.GetCommand(devId);

            // для JObject не срабатывает Equals
            BsonDocument doc1 = BsonDocument.Parse(command_1.ToString());
            BsonDocument doc2 = BsonDocument.Parse(command_2.ToString());

            Assert.IsTrue(doc1.Equals(doc2));
        }




        [TestMethod]
        public void RequestProcessingTest()
        {
            IKernel kernel = new StandardKernel();
            kernel.Bind<IDSRWebServiceController>().To<DSRWebServiceController>();
            kernel.Bind<Models.IDataContextAbstract>().To<Context.FakeContext>();

            Stream requestStream = new MemoryStream();
            Stream reponseStream = new MemoryStream();

            Mock<HttpRequestBase> request = new Mock<HttpRequestBase>();
            request.SetupGet((s) => s.InputStream).Returns(requestStream);

            Mock<HttpResponseBase> response = new Mock<HttpResponseBase>();
            response.Setup((s) => s.Flush()).Callback(() => { });
            response.Setup((s) => s.Clear()).Callback(() => { });

            Mock<HttpContextBase> mContext = new Mock<HttpContextBase>();
            mContext.SetupGet((r) => r.Request).Returns(request.Object);
            mContext.SetupGet((r) => r.Response).Returns(response.Object);

            IDataContextAbstract context = new FakeContext();
            DSRWebServiceController controller = (DSRWebServiceController)kernel.Get<IDSRWebServiceController>();

            controller.ControllerContext = new ControllerContext(mContext.Object, new RouteData(), controller);

            String deviceId = "devIdtest";
            int seconds = 0;
            bool polling = true;



            JObject commandOnGet = null;
            controller.onRequestProcessed += delegate(JObject newCommand)
            {
                commandOnGet = newCommand;
                polling = false;
            };

            JObject command = new JObject();
            command.Add("deviceId", deviceId);

            JObject commandPr = new JObject();
            commandPr.Add("commandName", "getInfo");
            commandPr.Add("parameters",new JArray());
            command.Add("command", commandPr);


            Byte[] commandBody = Encoding.UTF8.GetBytes(command.ToString());

            Task getTask = Task.Factory.StartNew(() =>
            {
                controller.Command(deviceId, 10);
            });


            while (polling)
            {
                Thread.Sleep(1000);
                seconds++;
                if (seconds == 3)
                {
                    controller.Request.InputStream.Position = 0;
                    controller.Request.InputStream.Write(commandBody, 0, commandBody.Length);
                    controller.NewCommand();
                }
            }

            Assert.IsTrue(seconds >= 3 && seconds <= 4);
            Assert.AreEqual(command.ToString(), commandOnGet.ToString());


            seconds = 0;
            polling = true;
            commandOnGet = null;
            getTask = Task.Factory.StartNew(() =>
            {
                controller.Command(deviceId, 10);
                polling = false;
            });

            while (polling)
            {
                Thread.Sleep(1000);
                seconds++;
            }
            Assert.IsTrue(seconds >= 9 && seconds <= 11);
            Assert.IsNull(commandOnGet);
        }
    }
}

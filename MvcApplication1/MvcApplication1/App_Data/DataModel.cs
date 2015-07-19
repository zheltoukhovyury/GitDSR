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
using System.Configuration;

namespace MvcApplication1.App_Data
{
    public class ViewContext
    {
        public List<JObject> history { get; set; }
        public String deviceIdForLogRequest { get; set; }
    }

    public interface IDataContextAbstract
    {
        void NewCommand(JObject command);
        JObject GetCommand(String deviceId);
    }


    public class DataContextRealiztion : IDataContextAbstract
    {
        public static DataContextRealiztion globalModelInstance = new DataContextRealiztion();
        MongoClient client;
        IMongoDatabase dataBase;
        String RabbitMQAddr = ConfigurationManager.AppSettings["RabbitMqHost"];
        String MongoDbConnectionString = ConfigurationManager.AppSettings["MongoDbConnectionString"];
        IMongoCollection<BsonDocument> collection;
        ConnectionFactory factory;

        public DataContextRealiztion()
        {
            //RabbitMq Init
            factory = new ConnectionFactory() { HostName = RabbitMQAddr };
            try
            {
                using (var connection = factory.CreateConnection())
                using (var channel = connection.CreateModel())
                {
                    Console.WriteLine("[+] Connecting to RabbitMQ service at {0} OK", RabbitMQAddr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Connecting to RabbitMQ service at {0} failed!", RabbitMQAddr, ex.Message);
            }

            //Mongo Init
            client = new MongoClient(MongoDbConnectionString);
            dataBase = client.GetDatabase(ConfigurationManager.AppSettings["MongoDbDataBaseName"]);
            collection = dataBase.GetCollection<BsonDocument>(ConfigurationManager.AppSettings["MongoDbCollectionName"]);
            var testCollection = dataBase.GetCollection<BsonDocument>("DBConnectionTest");
            BsonDocument testCommand = new BsonDocument();
            testCommand.Add("test", "test");
            try
            {
                Task operation = new Task(() =>
                {
                    testCollection.InsertOneAsync(testCommand);
                });
                operation.RunSynchronously();

                Console.WriteLine("[+] Connecting to MongoDB with ConnectionString {0} OK", MongoDbConnectionString);
            }
            catch (Exception)
            {
                Console.WriteLine("[-] Connecting to MongoDB with ConnectionString {0} FAILED", MongoDbConnectionString);
            }
            globalModelInstance = this;
        }

        public void NewCommand(JObject command)
        {

            command.Add("creationTime", DateTime.Now);
            command.Add("timeout", 60);
            command.Add("expired", false);
            command.Add("delivered", false);
            String json = command.ToString();
            String deviceId = command.GetValue("deviceId").ToString();
            BsonDocument doc = BsonDocument.Parse(json);
            Task operation = new Task(() =>
            {
                collection.InsertOneAsync(doc);
            });
            operation.RunSynchronously();

            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.ExchangeDeclare("ex" + deviceId, ExchangeType.Direct);
                    channel.QueueDeclare(deviceId, false, false, false, null);
                    channel.QueueBind(deviceId, "ex" + deviceId, "", null);
                    
                    IBasicProperties props = channel.CreateBasicProperties();
                    props.DeliveryMode = 2;
                    
                    Byte[] body = Encoding.UTF8.GetBytes(json);
                    channel.BasicPublish("ex" + deviceId, "", props, body);
                }
            }

            //объект в методе изменяется, и не является сериализуемым. копию сделать так просто не получится
            command.Remove("creationTime");
            command.Remove("timeout");
            command.Remove("expired");
            command.Remove("delivered");

        }

        public JObject GetCommand(String deviceId)
        {
            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {

                    channel.ExchangeDeclare("ex" + deviceId, ExchangeType.Direct);
                    channel.QueueDeclare(deviceId, false, false, false, null);
                    channel.QueueBind(deviceId, "ex" + deviceId, "", null);

                    BasicGetResult res = channel.BasicGet(deviceId, false);
                    if (res != null)
                    {
                        channel.BasicAck(res.DeliveryTag, false);
                        JObject command = JObject.Parse(Encoding.UTF8.GetString(res.Body));
                        command.Remove("creationTime");
                        command.Remove("timeout");
                        command.Remove("expired");
                        command.Remove("delivered");
                        return command;
                    }
                    else
                        return null;
                    
                }
            }
        }
    }
}

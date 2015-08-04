using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Web.Routing;
using System.Configuration;
using MvcApplication1.Context;


namespace MvcApplication1.Controllers
{

    public interface IDSRWebServiceController
    {
        [HttpGet]
        ActionResult Command(String deviceId, int timeout);
        [HttpPost]
        void NewCommand();
    }



    public class DSRWebServiceController : Controller, IDSRWebServiceController
    {
        Models.IDataContextAbstract rabbitContext;
        Models.IDataContextAbstract mongoContext;
        delegate void NewCommandSignal(String deviceId);
        static event NewCommandSignal onNewCommand;

        public delegate void RequestProcessed(JObject command);
        public event RequestProcessed onRequestProcessed;

        public DSRWebServiceController()
        {
            
        }

        [HttpGet]
        public ActionResult Index()
        {
            return View("Home");
        }

        [HttpGet]
        public ViewResult LogView()
        {
            return View("LogView", new Models.ViewContext());
        }

        [HttpPost]
        public ViewResult LogView(Models.ViewContext context)
        {
            mongoContext = Context.MongoDBContext.MongoContextFactory.GetContext();
            List<JObject> history = mongoContext.GetHistory(context.deviceIdForLogRequest);
            context.history = new List<Models.DSRCommand>();

            foreach (JObject historyItem in history)
            {
                Models.DSRCommand historyCommand = (Models.DSRCommand)historyItem.ToObject(typeof(Models.DSRCommand));
                context.history.Add(historyCommand);
            }
            return View("LogView", context);
        }


        [HttpGet]
        public ActionResult Command(String deviceId, int timeout = 60)
        {
            EventWaitHandle waitHandle;


            using (mongoContext = Context.MongoDBContext.MongoContextFactory.GetContext())
            using (rabbitContext = Context.RabbitMqContext.RabbitMqContextFactory.GetContext())
            {
                JObject command = rabbitContext.GetCommand(deviceId);
                if (command == null)
                {
                    
                    //загрузка ЦП на самом деле не меняется абсолютно
                    waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

                    NewCommandSignal sign = delegate(String devId)
                    {
                        if (deviceId == devId)
                        {
                            command = rabbitContext.GetCommand(deviceId);
                            waitHandle.Set();
                        }
                    };


                //на момент написания этого кода у меня похоже недостаточно опыта в WCF чтобы элегантно реализвать long polling
                // в асинхронном виде. да еще встроить в ASP.net контроллер. есть в документации аттрбут AsyncPattern для контракта. если удасться, к понедельнику переделаю
                //c использованием WCF. хотя что-то мне подсказывает, что я должен был сделать не на ASP.net при использовании WCF, а просто на WCF
                    
                    onNewCommand += sign;

                    waitHandle.WaitOne((Int32)timeout * 1000);

                    onNewCommand -= sign;

                    if (onRequestProcessed != null)
                        onRequestProcessed.Invoke(command);
                    if (command != null)
                    {
                        return Json((Models.DSRCommand)command.ToObject(typeof(Models.DSRCommand)), JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        return Json(null, JsonRequestBehavior.AllowGet);
                    }
                }
                else
                {
                    String js = command.ToString();
                    if (onRequestProcessed != null)
                        onRequestProcessed.Invoke(command);

                    return Json((Models.DSRCommand)command.ToObject(typeof(Models.DSRCommand)), JsonRequestBehavior.AllowGet);
                }
            }

        }
        [HttpPost]
        public void NewCommand()
        {
            using (mongoContext = Context.MongoDBContext.MongoContextFactory.GetContext())
            using (rabbitContext = Context.RabbitMqContext.RabbitMqContextFactory.GetContext())
            {
                bool valid = true;
                Byte[] body = new Byte[Request.InputStream.Length];
                Request.InputStream.Position = 0;
                Request.InputStream.Read(body, 0, body.Length);
                JObject newCommand = JObject.Parse(Encoding.UTF8.GetString(body));

                Models.DSRCommand newDsrCommand = (Models.DSRCommand)newCommand.ToObject(typeof(Models.DSRCommand));

                if (newDsrCommand.command.parameters != null)
                {
                    foreach (Models.DSRCommand.Command.Parameter paramter in newDsrCommand.command.parameters)
                    {
                        if (paramter.name == null || paramter.value == null || paramter.name == "")
                            valid = false;
                    }
                }
                else
                    valid = false;

                if (
                    valid == false ||
                    newDsrCommand.deviceId == null ||
                    newDsrCommand.command.commandName == null || Enum.GetNames(typeof(Models.DSRCommand.CommandList)).Contains<String>(newDsrCommand.command.commandName) == false ||
                    newDsrCommand.command.parameters == null)
                {
                    Response.StatusCode = 406;
                    Response.Clear();
                    Response.Flush();
                    return;
                }

                rabbitContext.NewCommand(newCommand);
                
                mongoContext.NewCommand(newCommand);

                if (onNewCommand != null)
                    onNewCommand.Invoke(newCommand.GetValue("deviceId").ToString());

                Response.Clear();
                Response.Flush();
            }
        }
    }
}

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
        Models.IDataContextAbstract context;
        delegate void NewCommandSignal(String deviceId);
        static event NewCommandSignal onNewCommand;

        public delegate void RequestProcessed(JObject command);
        public event RequestProcessed onRequestProcessed;

        public DSRWebServiceController(Models.IDataContextAbstract context)
        {
            this.context = context;
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
            List<JObject> history = this.context.GetHistory(context.deviceIdForLogRequest);
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
            JObject command = context.GetCommand(deviceId);
            if (command == null)
            {
                bool polling = true;
                Timer pollingTimer = null;

                pollingTimer = new System.Threading.Timer(delegate(Object state)
                {
                    if (pollingTimer != null)
                    {
                        pollingTimer.Change(-1, Timeout.Infinite);
                        pollingTimer.Dispose();
                        pollingTimer = null;
                        polling = false;
                    }
                }, null, (Int32)timeout * 1000, timeout * 1000);


                NewCommandSignal sign = delegate(String devId)
                {
                    if (deviceId == devId)
                    {
                        pollingTimer.Change(-1, Timeout.Infinite);
                        pollingTimer.Dispose();
                        pollingTimer = null;
                        
                        command = context.GetCommand(deviceId);
                        polling = false;
                    }
                };

                onNewCommand += sign;

                while (polling)
                    Thread.Sleep(10);

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
                if (onRequestProcessed!=null)
                    onRequestProcessed.Invoke(command);

                return Json((Models.DSRCommand)command.ToObject(typeof(Models.DSRCommand)), JsonRequestBehavior.AllowGet);
            }


        }
        [HttpPost]
        public void NewCommand()
        {
            Byte[] body = new Byte[Request.InputStream.Length];
            Request.InputStream.Position = 0;
            Request.InputStream.Read(body,0,body.Length);
            JObject newCommand = JObject.Parse(Encoding.UTF8.GetString(body));

            Models.DSRCommand newDsrCommand = (Models.DSRCommand)newCommand.ToObject(typeof(Models.DSRCommand));
            if (newDsrCommand.deviceId == null || newDsrCommand.command.commandName == null || newDsrCommand.command.parameters == null)
            {
                Response.StatusCode = 406;
                Response.Clear();
                Response.Flush();
                return;
            }

            context.NewCommand(newCommand);


            if (onNewCommand != null)
                onNewCommand.Invoke(newCommand.GetValue("deviceId").ToString());

            Response.Clear();
            Response.Flush();
        }
    }
}

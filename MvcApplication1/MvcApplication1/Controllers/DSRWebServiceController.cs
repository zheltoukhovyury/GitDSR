using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading;
using MvcApplication1.App_Data;
using System.Web.Routing;
using System.Configuration;


namespace MvcApplication1.Controllers
{

    public interface IDSRWebServiceController
    {
        [HttpGet]
        void Command(String deviceId, int timeout);
        [HttpPost]
        void NewCommand();
    }



    public class DSRWebServiceController : Controller, IDSRWebServiceController
    {
        App_Data.IDataContextAbstract context;
        delegate void NewCommandSignal(String deviceId);
        static event NewCommandSignal onNewCommand;

        public delegate void RequestProcessed(JObject command);
        public event RequestProcessed onRequestProcessed;

        public DSRWebServiceController(App_Data.IDataContextAbstract context)
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
            return View("LogView",new App_Data.ViewContext());
        }

        [HttpPost]
        public ViewResult LogView(App_Data.ViewContext context)
        {
            List<JObject> history = this.context.GetHistory(context.deviceIdForLogRequest);
            context.history = new List<DSRCommand>();

            foreach (JObject historyItem in history)
            {
                DSRCommand historyCommand = (DSRCommand)historyItem.ToObject(typeof(DSRCommand));
                context.history.Add(historyCommand);
            }
            return View("LogView", context);
        }


        [HttpGet]
        public void Command(String deviceId, int timeout = 60)
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
                Response.Clear();
                if (command != null)
                {
                    Response.ContentType = "application/json";
                    Response.BinaryWrite(Encoding.UTF8.GetBytes(command.ToString()));
                }
                Response.Flush();
                if (onRequestProcessed != null)
                    onRequestProcessed.Invoke(command);
            }
            else
            {
                String js = command.ToString();
                Response.ContentType = "application/json";
                Response.Clear();
                Response.BinaryWrite(Encoding.UTF8.GetBytes(command.ToString()));
                Response.Flush();
                if (onRequestProcessed!=null)
                    onRequestProcessed.Invoke(command);

            }
        }
        [HttpPost]
        public void NewCommand()
        {
            Byte[] body = new Byte[Request.InputStream.Length];
            Request.InputStream.Position = 0;
            Request.InputStream.Read(body,0,body.Length);
            JObject newCommand = JObject.Parse(Encoding.UTF8.GetString(body));

            context.NewCommand(newCommand);


            if (onNewCommand != null)
                onNewCommand.Invoke(newCommand.GetValue("deviceId").ToString());

            Response.Clear();
            Response.Flush();
        }
    }
}

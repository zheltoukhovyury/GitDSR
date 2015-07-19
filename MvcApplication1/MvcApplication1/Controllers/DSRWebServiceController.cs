using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading;

 

namespace MvcApplication1.Controllers
{
    public class DSRWebServiceController : Controller
    {
        App_Data.DataContextRealiztion context;
        delegate void NewCommandSignal(String deviceId);
        static event NewCommandSignal onNewCommand;

        public DSRWebServiceController()
        {
            context = App_Data.DataContextRealiztion.globalModelInstance;
        }

        [HttpGet]
        public ActionResult Index()
        {
            return View("Home");
        }

        [HttpGet]
        public ActionResult LogView()
        {
            return View("Home");
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
                        polling = false;
                        command = context.GetCommand(deviceId);
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
            }
            else
            {
                String js = command.ToString();
                Response.ContentType = "application/json";
                Response.Clear();
                Response.BinaryWrite(Encoding.UTF8.GetBytes(command.ToString()));
                Response.Flush();

            }
        }
        [HttpPost]
        public void NewCommand(String json)
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

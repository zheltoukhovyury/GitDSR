using MvcApplication1.Controllers;
using Ninject;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;

namespace MvcApplication1
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801
    public class MvcApplication : System.Web.HttpApplication
    {
        /*
        class ControllerFactory : DefaultControllerFactory
        {

            IKernel kernel;
            public ControllerFactory()
            {
                kernel = new StandardKernel();
                kernel.Bind<Models.IDataContextAbstract>().To<Context.DataContextRealiztion>();

                // не осилил как нужно сделать привязку класса контроллера чтобы конструкотор контроллера вызывался с аргументом из привязки контекста
                //так что контекст создается здесь
                kernel.Bind<DSRWebServiceController>().ToSelf().WithConstructorArgument("context", new Context.DataContextRealiztion(
                    RabbitMQAddr: ConfigurationManager.AppSettings["RabbitMqHost"],
                    MongoDbConnectionString: ConfigurationManager.AppSettings["MongoDbConnectionString"],
                    MongoDbDataBaseName: ConfigurationManager.AppSettings["MongoDbDataBaseName"],
                    MongoDbCollectionName: ConfigurationManager.AppSettings["MongoDbCollectionName"]));
            }
            


            protected override IController GetControllerInstance(RequestContext requestContext, Type controllerType)
            {
                return (IController)kernel.Get(controllerType);
            }
        
        }
        */

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            WebApiConfig.Register(GlobalConfiguration.Configuration);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            //ControllerBuilder.Current.SetControllerFactory(new ControllerFactory());
        }
    }
}
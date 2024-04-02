using ClientServices.Interfaces;

namespace ClientServices.Services;

public class MessagesRestService: RestServiceBase, IMessagesService
{
    public MessagesRestService(IRestService restService) : base(restService)
    {
    }
}
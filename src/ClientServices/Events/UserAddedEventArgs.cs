using Model.DTO;

namespace ClientServices.Events;

public class UserAddedEventArgs: EventArgs
{
  public UserListing? User { get; set; }
}
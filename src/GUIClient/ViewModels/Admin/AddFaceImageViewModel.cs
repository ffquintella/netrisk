using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin;

public class AddFaceImageViewModel: ViewModelBase
{
    #region PROPERTIES
        private int UserId { get; set; }
        private Window ParentWindow { get; set; }
        
        private Bitmap _image;

        public Bitmap Image
        {
            get => _image;
            set => this.RaiseAndSetIfChanged(ref _image, value);
        }
        
    #endregion
    
    
    #region SERVICES
    
    #endregion
    
    
    public AddFaceImageViewModel(int userId, Window parentWindow)
    {
        ParentWindow = parentWindow;
        UserId = userId;
        Image = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/placeholder.png")));
    }


    
}
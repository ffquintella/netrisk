using System.Text;
using System.Text.Json;
using Contracts;
using Contracts.Exceptions;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.FaceID;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;
using SkiaSharp;
using Tools.Extensions;
using Tools.Images;
using Tools.Math;
using Tools.Security;
using Tools.Serialization;

namespace ServerServices.Services;

public class FaceIDService(
    ILogger logger,
    IDalService dalService,
    IPluginsService pluginsService,
    IUsersService usersService,
    IEnvironmentService environmentService)
    : ServiceBase(logger, dalService), IFaceIDService
{
    
    private IEnvironmentService EnvironmentService { get; set; } = environmentService;
    private IPluginsService PluginsService { get; } = pluginsService;
    private IUsersService UsersService { get; } = usersService;

    public async Task<bool> IsFaceIDPluginEnabled()
    {
        var faceIdPluginExists = await PluginsService.PluginExistsAsync("FaceIdPlugin");
        
        bool faceIdPluginEnabled = false;
        if (faceIdPluginExists)
        {
            faceIdPluginEnabled = await PluginsService.PluginIsEnabledAsync("FaceIdPlugin");
        }

        return faceIdPluginEnabled;
    }
    
    public async Task<ServiceInformation> GetInfoAsync()
    {
        
        var faceIDPluginExists = await PluginsService.PluginExistsAsync("FaceIdPlugin");
        
        
        var information = new ServiceInformation
        {
            IsServiceAvailable = await IsFaceIDPluginEnabled(),
            ServiceName = "FaceId",
            ServiceVersion = "1.0",
            ServiceDescription = "FaceID service for user authentication",
            ServiceUrl = "/faceid",
            ServiceNeedsPlugin = true,
            ServicePluginInstalled = faceIDPluginExists
        };

        return information;
        
    }

    public async Task<bool> IsUserEnabledAsync(int userId)
    {
        
        if(!await IsFaceIDPluginEnabled()) 
            return false;
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        await using var context = DalService.GetContext();
        
        var user = await context.FaceIDUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
        if (user == null)
        {
            return false;
        }
        return user.IsEnabled;
        
    }

    public  async Task<bool> UserHasFaceSetAsync(int userId)
    {
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        await using var context = DalService.GetContext();
        
        var user = await context.FaceIDUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
        if (user == null)
        {
            return false;
        }
        
        return user.FaceIdentification.Length > 0;
    }

    public async Task SetUserEnabledStatusAsync(int userId, bool enabled, int loggedUserId)
    {
        if (!await IsFaceIDPluginEnabled()) return;
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        await using var context = DalService.GetContext();
        
        var faceIdUser = await context.FaceIDUsers
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (faceIdUser == null)
        {
            // If it does not exist, create it
            faceIdUser = new FaceIDUser
            {
                UserId = userId,
                IsEnabled = enabled,
                SignatureSeed = SeedGenerator.GenerateUniqueSeedBase64(),
                LastUpdateUserId = loggedUserId,
                LastUpdate = DateTime.Now
            };
            
            context.FaceIDUsers.Add(faceIdUser);
        }
        else
        {
            faceIdUser.IsEnabled = enabled;
            faceIdUser.LastUpdate = DateTime.Now;
            faceIdUser.LastUpdateUserId = loggedUserId;
            context.FaceIDUsers.Update(faceIdUser);
        }
        
        await context.SaveChangesAsync();
    }

    public async Task<string> SaveFaceIdAsync(int userId, FaceData faceData,  int loggedUserId)
    {
        if(faceData.ImageType != "jpg" && faceData.ImageType != "png" &&  faceData.ImageType != "SKBitmap")
        {
            throw new Exception("Face image must be a SKBitmap, jpg or png");
        }

        
        if (!await IsFaceIDPluginEnabled()) return "";
        
        var faceIdPlugin = await PluginsService.GetPluginAsync<INetriskFaceIDPlugin>("FaceIdPlugin");
        if (faceIdPlugin == null)
        {
            throw new Exception("FaceId plugin not found");
        }
        
        //Initializing the plugin 
        faceIdPlugin.Initialize(Logger);
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }

        //Check if the user has faceid enabled
        if (!await IsUserEnabledAsync(userId))
        {
            throw new UserNotFoundException($"User with id {userId} has not been enabled");
        }

        SKBitmap? skFace = null;
        
        
        if(faceData.ImageType == "SKBitmap")
        {
            if(faceData.FaceImageJson == null) throw new Exception("Face image is null");
            skFace = faceData.FaceImageJson.FromJson();
        }

        if (faceData.ImageType == "png")
        {
            if(faceData.FaceImageB64 == null) throw new Exception("Face image is null");
            skFace = faceData.FaceImageB64.FromBase64Png();
        }
        
        if (faceData.ImageType == "jpg")
        {
            if(faceData.FaceImageB64 == null) throw new Exception("Face image is null");
            byte[] imageBytes = Convert.FromBase64String(faceData.FaceImageB64);
       
            using var stream = new SKMemoryStream(imageBytes);
            skFace =  SKBitmap.Decode(stream);
        }
            
        

        if (skFace == null)
        {
            throw new Exception("Face data is null");
        }
        
        var face = faceIdPlugin.ExtractFace(skFace);
        
        if (face == null)
        {
            throw new FaceDetectionException("Face not detected");
        }

        var descriptor = faceIdPlugin.ExtractEncodings(face);
        
        if(descriptor == null) 
            throw new FaceDetectionException("Face descriptor is null");

        await using var context = DalService.GetContext();

        var descriptor64 = descriptor.ToBase64();
        
        // Let's get the faceid user 
        
        var faceIdUser = await context.FaceIDUsers
            .FirstOrDefaultAsync(x => x.UserId == userId);
        
        if (faceIdUser == null) throw new UserNotFoundException($"User with id {userId} has not been enabled");
        
        faceIdUser.FaceIdentification = descriptor64;
        faceIdUser.LastUpdate = DateTime.Now;
        faceIdUser.LastUpdateUserId = loggedUserId;
        
        await context.SaveChangesAsync();
        
        return descriptor64;

    }

    public async Task<FaceTransactionData> StartTransactionAsync(int userId)
    {
        await using var context = DalService.GetContext();

        var guid = Guid.NewGuid();
        
        
        // Check if the guid already exists in the database
        while (await context.BiometricTransactions.AnyAsync(x => x.TransactionId == guid))
        {
            guid = Guid.NewGuid();
        }
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        // Check if the user has faceid enabled
        if (!await IsUserEnabledAsync(userId))
        {
            throw new UserNotFoundException($"User with id {userId} has not been enabled");
        }
        
        // Gets the faceid user
        
        var faceIdUser = await context.FaceIDUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (faceIdUser == null)
        {
            throw new UserNotFoundException($"User with id {userId} has no faceId set");
        }
        
        var seedBytes =  Convert.FromBase64String(faceIdUser.SignatureSeed);
        
        var biomryticTemplate = "---" + DateTime.Now.ToString("yyyyMMddHHmmssss") + "---";
        
        var transactionData = "---" + DateTime.Now.ToString("yyyyMMddHHmmssss") + "---" + userId + "---" + faceIdUser.Id + "---" + guid.ToString() + "---";

        var biometricAnchor = BiometricTools.CreateBiometricAnchor(seedBytes, Encoding.UTF8.GetBytes( biomryticTemplate), transactionData);
        
        
        // Create a new FaceTransactionData object
        // and set the userId, transactionId and startTime
        // Also, we will create a new BiometricTransaction object
        var transaction = new BiometricTransaction
        {
            UserId = userId,
            FaceIdUserId = faceIdUser.Id,
            BiometricType = "FaceId",
            StartTime = DateTime.Now,
            TransactionId = guid,
            TransactionDetails = transactionData,
            TransactionResult = TransactionResult.Unknown,
            BiometricLivenessAnchor = biometricAnchor
        };
        
        // Check if the user has faceid set
        
        if (!await UserHasFaceSetAsync(userId))
        {
            transaction.TransactionResult = TransactionResult.UserDoesNotHaveFaceId;
            transaction.TransactionResultDetails = $"User with id {userId} has not set a faceid";
            context.BiometricTransactions.Add(transaction);
            await context.SaveChangesAsync();
            
            throw new UserNotFoundException($"User with id {userId} has not set a faceid");
        }
        
        transaction.TransactionResult = TransactionResult.SuccessfullyStarted;
        transaction.TransactionResultDetails = $"User with id {userId} started a transaction";
        
        transaction.ValidationSequence = await GenerateRandomValidationSequenceAsync(6);
        
        // Save the transaction to the database
        context.BiometricTransactions.Add(transaction);
        await context.SaveChangesAsync();
        
        var ftd = new FaceTransactionData
        {
            UserId = userId,
            TransactionId = transaction.TransactionId.Value,
            StartTime = transaction.StartTime,
            ValidationSequence = transaction.ValidationSequence
        };

        return ftd;
    }

    public async Task CleanUpExpiredTransactionsAsync()
    {
        await using var context = DalService.GetContext();

        var expirationMinutes = 5;
        
        // Define the expiration time (e.g., 10 minutes)
        var expirationTime = DateTime.Now.AddMinutes(-expirationMinutes);
        
        // Close transactions with unknown result
        var unknownTransactions = await context.BiometricTransactions
            .Where(t => t.StartTime < expirationTime && t.TransactionResult == TransactionResult.Unknown)
            .ToListAsync();

        foreach (var transaction in unknownTransactions)
        {
            transaction.TransactionResult = TransactionResult.Failed;
            transaction.TransactionResultDetails = "Transaction expired due to unknown result";
            transaction.ResultTime = DateTime.Now;
            context.BiometricTransactions.Update(transaction);
        }
        
        await context.SaveChangesAsync();
        
        expirationTime = DateTime.Now.AddMinutes(-expirationMinutes);
        
        // Find all transactions older than the expiration time
        var expiredTransactions = await context.BiometricTransactions
            .Where(t => t.StartTime < expirationTime 
                        && t.TransactionResult != TransactionResult.RequestTimeout  )
            .ToListAsync();
        
        // Close expired transactions
        foreach (var transaction in expiredTransactions)
        {
            transaction.TransactionResult = TransactionResult.RequestTimeout;
            transaction.TransactionResultDetails = "Transaction expired";
            transaction.ResultTime = DateTime.Now;
            context.BiometricTransactions.Update(transaction);
        }
        
        await context.SaveChangesAsync();
    }

    public async Task<FaceToken> CommitTransactionAsync(int userId, FaceTransactionData faceTData, string? transactionObjectType, string? transactionObjectId = null)
    {

        // Let's check if the transaction exists 
        
        await using var context = DalService.GetContext();
        
        var transaction = await context.BiometricTransactions
            .FirstOrDefaultAsync(x => x.TransactionId == faceTData.TransactionId && x.UserId == userId);
        
        if(transaction == null) 
        {
            Log.Warning("Transaction with id {transactionId} does not exist", faceTData.TransactionId);
            throw new ArgumentException($"Transaction with ID {faceTData.TransactionId} for user {userId} does not exist", nameof(faceTData.TransactionId));
        }
        
        
        if(faceTData.SequenceImages == null) 
            throw new ArgumentNullException(nameof(faceTData.SequenceImages), "Sequence images cannot be null");
        
        var imageList = JsonSerializer.Deserialize<List<ImageCaptureData>>(faceTData.SequenceImages);
        
        
        // Since we are check at the client we can assume the imageas all have a face
        
        if (imageList == null || imageList.Count == 0)
        {
            throw new ArgumentException("Sequence images cannot be empty", nameof(faceTData.SequenceImages));
        }
        
        if (!await IsFaceIDPluginEnabled()) throw new PluginDisabledException("FaceID is disabled");
        
        INetriskFaceIDPlugin plugin;
        
        try
        {
            plugin = await PluginsService.GetPluginAsync<INetriskFaceIDPlugin>("FaceIdPlugin");
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to find plugin");
            throw;
        }
        
        if (plugin == null)
        {
            throw new NullReferenceException("plugin");
        }
        
        // Initialize the plugin
        plugin.Initialize(Logger);
        
        //Now we will process the images
        // The first image is the off illumination image witch we will use to identify the user

        SKBitmap? offIlluminationImage;
        float[]? descriptor = null;
        try
        {
            if(imageList == null) throw new NullReferenceException("imageList cannot be null");
            if (imageList.Count == 0) throw new ArgumentException("imageList cannot be empty");
            if (imageList[0].PngImageData == null) throw new ArgumentNullException("imageList[0].PngImageData", "PngImageData cannot be null");
            if (imageList[0].PngImageData.Length == 0) throw new ArgumentException("PngImageData cannot be empty", "PngImageData");
            offIlluminationImage = ImageTools.LoadBitmapFromPngBytes(imageList[0].PngImageData);
            
            if (offIlluminationImage == null)
            {
                throw new Exception("Failed to load off illumination image");
            }
            
            var face = plugin.ExtractFace(offIlluminationImage);
            if (face == null)
            {
                throw new FaceDetectionException("Face not detected in off illumination image");
            }
            descriptor = plugin.ExtractEncodings(face);
            if (descriptor == null)
            {
                throw new FaceDetectionException("Face descriptor is null in off illumination image");
            }
            
            // Locate the face in the database
            var userIdFound = await LocateFaceAsync(descriptor);
            
            if(userIdFound < 0)
            {
                throw new UserNotFoundException("User not found with the provided face descriptor");
            }
            
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to load image");
            throw new Exception("Failed to load off illumination image", e);
        }
        
        // Now that we have found the user we need to verify for spoofing attacks TODO

        // First check against the classical spoof detector 
        
        
        
        // We will use the secret key to create a biometric authentication token for the user
        var secretKey = Convert.FromBase64String(EnvironmentService.ServerSecretToken);

        var biometricToken = BiometricTools.CreateBiometricToken(secretKey, faceTData.TransactionId.ToString(), userId);
        

        // Update the transaction with the result
        
        var faceIdUser = await context.FaceIDUsers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
        
        transaction.TransactionResult = TransactionResult.SuccessfullyCompleted;
        transaction.ResultTime = DateTime.Now;
        var tdata = transaction.TransactionDetails ?? $"{Guid.NewGuid():N}"; 
        transaction.BiometricLivenessAnchor = BiometricTools.CreateBiometricAnchor(Encoding.UTF8.GetBytes(faceIdUser.SignatureSeed),
            descriptor.ToByteArray(),
            tdata);
        transaction.TransactionObjectType = transactionObjectType;
        transaction.ValidationObjectData = faceTData.SequenceImages;

        if (transactionObjectId == null) transaction.TransactionObjectId = null;
        else
        {
            bool isInteger = int.TryParse(transactionObjectId, out int result);
            if(isInteger) transaction.TransactionObjectId = result;
        }
        
        await context.SaveChangesAsync();

        return biometricToken;
    }

    public async Task<bool> FaceTokenIsValidAsync(int userId, FaceToken faceToken, string transactionId)
    {
        var secretKey = EnvironmentService.ServerSecretToken;
        
        if (string.IsNullOrEmpty(secretKey))
        {
            throw new ArgumentException("Server secret token is not set", nameof(secretKey));
        }
        
        var secretBytes = Convert.FromBase64String(secretKey);
        
        if (string.IsNullOrEmpty(faceToken?.Token))
        {
            throw new ArgumentException("Face token is null or empty", nameof(faceToken));
        }
        
        // Check if the transaction ID is valid
        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty", nameof(transactionId));
        }
        await using var context = DalService.GetContext();
        var transaction = await context.BiometricTransactions
            //.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TransactionId.ToString() == transactionId && x.UserId == userId);
        
        if(transaction == null) 
        {
            throw new ArgumentException($"Transaction with ID {transactionId} for user {userId} does not exist", nameof(transactionId));
        }
        
        // Validate the face toke
        var result = BiometricTools.ValidateBiometricToken(
            faceToken,
            secretBytes,
            transactionId,
            userId);
        
        //Update Status of the transaction
        if (!result)
        {
            transaction.TransactionResult = TransactionResult.RequestTimeout;
            transaction.ResultTime = DateTime.Now;
            await context.SaveChangesAsync();
        }
        

        return result;
    }

    private async Task<int> LocateFaceAsync(float[] descriptor, double threshold = 2.6)
    {
        
        await using var context = DalService.GetContext();
        var faceIdUsers = await context.FaceIDUsers
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.FaceIdentification != null && x.FaceIdentification.Length > 0).ToListAsync();
        if (faceIdUsers == null || faceIdUsers.Count == 0) return -1;
        
        var results = faceIdUsers.Select(u => new
            {
                UserId = u.UserId,
                FaceIdentification = ConvertFaceIdentificationToFloatArray(u.FaceIdentification),
                Distance = VectorComparasion.EuclideanDistance(ConvertFaceIdentificationToFloatArray(u.FaceIdentification), descriptor)
            }).OrderBy(r => r.Distance).ToList();
            
        if(results.Count < 1) return -1;
        
        if(results[0].Distance > threshold) return -1;
        
        // If the closest match is within the threshold, return the user ID
        return results[0].UserId;
        
    }
    
    private float [] ConvertFaceIdentificationToFloatArray(string faceIdentification)
    {
        if (string.IsNullOrEmpty(faceIdentification))
        {
            throw new ArgumentException("Face identification cannot be null or empty", nameof(faceIdentification));
        }
        
        // Convert the Base64 string to a byte array
        var bytes = Convert.FromBase64String(faceIdentification);
        
        // Convert the byte array to a float array
        var floatArray = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floatArray, 0, bytes.Length);
        
        return floatArray;
    }
    
    private async Task<List<char>> GenerateRandomValidationSequenceAsync(int length)
    {
        var possibleChars = "RGBW"; // Red, Green, Blue, White
        
        var random = new Random();
        var sequence = new List<char>(length);
        for (int i = 0; i < length; i++)
        {
            // Select a random character from the possible characters
            char randomChar = possibleChars[random.Next(possibleChars.Length)];
            sequence.Add(randomChar);
        }
        
        return sequence;
    }

    public async Task<(bool, Guid?)> ValidateTokenAndLocateTransaction(int userId, string faceToken)
    {
        var secretKey = EnvironmentService.ServerSecretToken;
        
        if (string.IsNullOrEmpty(secretKey))
        {
            throw new ArgumentException("Server secret token is not set", nameof(secretKey));
        }
        
        var secretBytes = Convert.FromBase64String(secretKey);

        if (string.IsNullOrEmpty(faceToken))
        {
            throw new ArgumentException("Face token is null or empty", nameof(faceToken));
        }
        
        await using var context = DalService.GetContext();

        var transactions = await context.BiometricTransactions
            .Where(x => x.UserId == userId && x.BiometricLivenessAnchor != null
                                           && x.BiometricLivenessAnchor.Length > 0
                                           && (x.TransactionResult == TransactionResult.SuccessfullyStarted ||
                                               x.TransactionResult == TransactionResult.SuccessfullyCompleted))
            .ToListAsync();


        bool isValid = false;
        Guid? trasactionId = null;
        foreach (var transaction in transactions)
        {
            trasactionId = transaction?.TransactionId;
        
            if (trasactionId == null)
            {
                throw new DataNotFoundException($"No transaction found for user {userId}", nameof(userId));
            }
        
            var faceTokenObj = new FaceToken
            {
                Token = faceToken,
            };
        
            // Validate the face token
            isValid = BiometricTools.ValidateBiometricToken(
                faceTokenObj,
                secretBytes,
                trasactionId.ToString()!,
                userId);
            
            if(isValid) break;
        }

        
        if(!isValid)
        {
            return (false, null);
        }
        
        // If the token is valid, return true and the transaction ID
        
        return (true, trasactionId);

    }

    public async Task<List<BiometricTransaction>> GetUserOpenTransactionsAsync(int userId)
    {
        await using var context = DalService.GetContext();
        var transactions = await context.BiometricTransactions
            .AsNoTracking()
            .Where(x => x.UserId == userId && 
                        (x.TransactionResult == TransactionResult.SuccessfullyCompleted ))
            .ToListAsync();
        
        return transactions;
    }
}
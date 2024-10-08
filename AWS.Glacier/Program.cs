using Amazon.Glacier;
using Amazon.Glacier.Model;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonGlacier>();
builder.Services.AddScoped<IDescriptionStorageService, DescriptionStorageService>();


var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/create-vault", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName) =>
{
    var vaultIfExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);


    if (vaultIfExistsModel.describeVaultResponse is not null)
        return Results.BadRequest("Vault already exists");


    var createVaultRequest = new CreateVaultRequest
    {
        VaultName = vaultName
    };

    var createVaultResponse = await _amazonGlacier.CreateVaultAsync(createVaultRequest);

    if (createVaultResponse.HttpStatusCode is not System.Net.HttpStatusCode.Created)
        return Results.BadRequest("Vault not created");


    return Results.Created();

});

app.MapGet("/list-vaults", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] int limit) =>
{
    var listVaultRequest = new ListVaultsRequest()
    {
        Limit = limit
    };

    var listVaultsResponse = await _amazonGlacier.ListVaultsAsync(listVaultRequest);

    return Results.Ok(listVaultsResponse.VaultList);


});

app.MapGet("/describe-vault", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName) =>
{

    var vaultIsExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);

    if (vaultIsExistsModel.describeVaultResponse is DescribeVaultResponse response)
        return Results.Ok(response);

    return Results.BadRequest(vaultIsExistsModel.errorMessage);

});


app.MapPost("/upload-archive", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromServices] IDescriptionStorageService _descriptionStorageService,
    [FromQuery] string vaultName,
    [FromForm] IFormFile file) =>
{

    var vaultIsExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);

    if (vaultIsExistsModel.describeVaultResponse is null)
        return Results.BadRequest(vaultIsExistsModel.errorMessage);



    var uploadArchieveRequest = new UploadArchiveRequest()
    {
        VaultName = vaultName,
        Body = file.OpenReadStream(),
        ArchiveDescription = $"{file.FileName}",
        Checksum = TreeHashGenerator.CalculateTreeHash(file.OpenReadStream()),
    };


    var uploadArchiveResponse = await _amazonGlacier.UploadArchiveAsync(uploadArchieveRequest);

    await _descriptionStorageService.WriteToStorage(new FileStorageModel(

        archiveId: uploadArchiveResponse.ArchiveId,
        fileDescription: uploadArchieveRequest.ArchiveDescription,
        fileName: file.FileName,
        vaultName: vaultName
    ));





    return Results.Ok(uploadArchiveResponse);


}).DisableAntiforgery();

app.MapPost("/initate-job", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName,
    [FromQuery] string archiveId) =>
{

    if ((await GetVaultIfExists(_amazonGlacier, vaultName)).describeVaultResponse is null)
        return Results.NotFound("vault not found");


    var initiateJobRequest = new InitiateJobRequest()
    {
        VaultName = vaultName,
        JobParameters = new JobParameters()
        {
            ArchiveId = archiveId,
            Type = "archive-retrieval", //inventory-retrieval
            Tier = "Standard" //Expedited ,Bulk , Standart
        }
    };

    InitiateJobResponse initiateJobResponse = default;

    try
    {
        initiateJobResponse = await _amazonGlacier.InitiateJobAsync(initiateJobRequest);
    }
    catch (ResourceNotFoundException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);

    }

    return Results.Ok(initiateJobResponse);

});

app.MapGet("list-jobs", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName) =>
{
    var vaultIsExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);

    if (vaultIsExistsModel.describeVaultResponse is null)
        return Results.BadRequest(vaultIsExistsModel.errorMessage);



    var listJobRequest = new ListJobsRequest()
    {
        Limit = 10,
        VaultName = vaultName
    };

    return Results.Ok(await _amazonGlacier.ListJobsAsync(listJobRequest));

});

app.MapGet("get-job-description", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string jobId,
    [FromQuery] string vaultName) =>
{


    var vaultExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);

    if (vaultExistsModel.describeVaultResponse is null)
        return Results.BadRequest(vaultExistsModel.errorMessage);

    var jobExistsModel = await GetJobIfExists(_amazonGlacier, vaultName, jobId);

    if (jobExistsModel.describeJobResponse is null)
        return Results.BadRequest(jobExistsModel.errorMessage);

    return Results.Ok(jobExistsModel.describeJobResponse);

});

app.MapGet("get-archive", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string jobId,
    [FromQuery] string vaultName) =>
{

    var vaultIsExistModel = await GetVaultIfExists(
                                            _amazonGlacier: _amazonGlacier,
                                            vaultName: vaultName);

    if (vaultIsExistModel.describeVaultResponse is null)
        return Results.BadRequest(vaultIsExistModel.errorMessage);



    var jobExistModel = await GetJobIfExists(
        _amazonGlacier: _amazonGlacier,
        vaultName: vaultName,
        jobId: jobId);

    if (jobExistModel.describeJobResponse is null)
        return Results.BadRequest(jobExistModel.errorMessage);


    var getJobOutputRequest = new GetJobOutputRequest()
    {
        JobId = jobId,
        VaultName = vaultName
    };

    var getJobOutputResponse = await _amazonGlacier.GetJobOutputAsync(getJobOutputRequest);

    var contentType = GetContentTypeForFileName(getJobOutputResponse.ArchiveDescription);


    return Results.File(getJobOutputResponse.Body, contentType, getJobOutputResponse.ArchiveDescription);


});

app.MapDelete("delete-vault", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName) =>
{
    var vaultIsExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);

    if (vaultIsExistsModel.describeVaultResponse is null)
        return Results.BadRequest(vaultIsExistsModel.errorMessage);

    var deleteVaultRequest = new DeleteVaultRequest()
    {
        VaultName = vaultName
    };
    var deleteVaultResponse = await _amazonGlacier.DeleteVaultAsync(deleteVaultRequest);



    return Results.Ok(deleteVaultResponse);
});

app.MapDelete("delete-archive", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string archiveId,
    [FromQuery] string vaultName) =>
{

    var vaultIsExistsModel = await GetVaultIfExists(_amazonGlacier, vaultName);

    if (vaultIsExistsModel.describeVaultResponse is null)
        return Results.BadRequest(vaultIsExistsModel.errorMessage);

    var deleteArchiveRequest = new DeleteArchiveRequest()
    {
        ArchiveId = archiveId,
        VaultName = vaultName
    };

    DeleteArchiveResponse deleteArchiveResponse = default;
    try
    {
        deleteArchiveResponse = await _amazonGlacier.DeleteArchiveAsync(deleteArchiveRequest);

    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }

    return Results.Ok(deleteArchiveResponse);
});


app.MapPost("write-file-archive-description-storage-local", async (
  [FromBody] FileStorageModel fileStorageModel,
  [FromServices] IDescriptionStorageService _descriptionStorageService) =>
{
    await _descriptionStorageService.WriteToStorage(fileStorageModel);
    return Results.Ok();
});

app.MapGet("read-file-archive-description-storage-local", async (
  [FromServices] IDescriptionStorageService _descriptionStorageService) =>
{
    return Results.Ok(await _descriptionStorageService.ReadToStorage());
});

app.MapGet("inventory-retrieval-by-vault-name", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName) =>
{

    var initiateJobRequest = new InitiateJobRequest
    {
        VaultName = vaultName,
        JobParameters = new JobParameters
        {
            Type = "inventory-retrieval"
        }
    };


    var response = await _amazonGlacier.InitiateJobAsync(initiateJobRequest);

    return Results.Ok(response);
});

app.Run();

static async Task<VaultIsExistsModel> GetVaultIfExists(
    IAmazonGlacier _amazonGlacier,
    string vaultName)
{
    DescribeVaultResponse? describeVaultResponse = null;
    string errorMessage = string.Empty;

    try
    {
        var describeVaultRequest = new DescribeVaultRequest()
        {
            VaultName = vaultName
        };
        describeVaultResponse = await _amazonGlacier.DescribeVaultAsync(describeVaultRequest);

    }
    catch (ResourceNotFoundException)
    {
        errorMessage = "vault not found";
    }
    catch (Exception ex)
    {
        errorMessage = ex.Message;
    }

    return new VaultIsExistsModel(
       vaultName: vaultName,
       errorMessage: errorMessage,
       describeVaultResponse: describeVaultResponse);

}

static async Task<JobIsExistsModel> GetJobIfExists(
    IAmazonGlacier _amazonGlacier,
    string vaultName,
    string jobId)
{
    DescribeJobResponse describeJobResponse = default;
    string errorMessage = default;

    try
    {

        var describeJobRequest = new DescribeJobRequest()
        {
            JobId = jobId,
            VaultName = vaultName
        };

        describeJobResponse = await _amazonGlacier.DescribeJobAsync(describeJobRequest);

    }
    catch (ResourceNotFoundException)
    {
        errorMessage = "job not found";

    }
    catch (Exception ex)
    {
        errorMessage = $"job not found: {ex.Message}";
    }


    return new JobIsExistsModel(
        errorMessage: errorMessage,
        vaultName: vaultName,
        describeJobResponse: describeJobResponse);

}


static FileInfoModel GetFileInfo(string fileName)
{

    var fileExtensionIndex = fileName.LastIndexOf(".");

    var fileExtension = fileName[++fileExtensionIndex..(fileName.Length)];

    return new FileInfoModel(
        fileType: fileExtension,
        fileName: fileName);
}

static string GetContentTypeForFileName(string fileName)
{
    var fileInfoModel = GetFileInfo(fileName);

    var contentType = fileInfoModel.fileType switch
    {
        "pdf" => "application/pdf",
        "png" => "image/png",
        "jpeg" => "image/jpeg",
        "jpg" => "image/jpeg",
        "html" => "text/html",
        "htm" => "text/html",
        "json" => "application/json",
        "xml" => "application/xml",
        "txt" => "text/plain",
        "csv" => "text/csv",
        "zip" => "application/zip",
        "mp4" => "video/mp4",
        "mp3" => "audio/mpeg",
        _ => "application/octet-stream",
    };

    return contentType;

}

internal record FileStorageModel(string fileName, string fileDescription, string archiveId, string vaultName);

internal record FileInfoModel(string fileName, string fileType);

internal record VaultIsExistsModel(
    string vaultName,
    string errorMessage,
    DescribeVaultResponse describeVaultResponse);

internal record JobIsExistsModel(
    string vaultName,
    string errorMessage,
    DescribeJobResponse describeJobResponse);


internal interface IDescriptionStorageService
{
    Task WriteToStorage(FileStorageModel fileStorageModel, string storageFileName = "storageDescription.txt");
    Task<IEnumerable<FileStorageModel>> ReadToStorage(string storageFileName = "storageDescription.txt");
}

internal class DescriptionStorageService : IDescriptionStorageService
{
    public async Task<IEnumerable<FileStorageModel>> ReadToStorage(string storageFileName = "storageDescription.txt")
    {
        string currentlyDirectory = Directory.GetCurrentDirectory();

        string filePath = Path.Combine(currentlyDirectory, storageFileName);

        using var fileStream = new FileStream(filePath, FileMode.OpenOrCreate);
        using var streamReader = new StreamReader(fileStream);

        string line = default;
        ICollection<FileStorageModel> files = new HashSet<FileStorageModel>();

        while ((line = await streamReader.ReadLineAsync()) is not null)
        {
            files.Add(JsonSerializer.Deserialize<FileStorageModel>(line));
        }

        return files;
    }

    public async Task WriteToStorage(FileStorageModel fileStorageModel, string storageFileName = "storageDescription.txt")
    {
        string currentlyDirectory = Directory.GetCurrentDirectory();

        string filePath = Path.Combine(currentlyDirectory, storageFileName);

        using var fileStream = new FileStream(filePath, FileMode.Append);
        using var streamWriter = new StreamWriter(fileStream);

        await streamWriter.WriteLineAsync(JsonSerializer.Serialize(fileStorageModel));

    }
}
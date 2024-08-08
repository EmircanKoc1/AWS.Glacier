using Amazon.Glacier;
using Amazon.Glacier.Model;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonGlacier>();



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
    try
    {
        var describeVaultRequest = new DescribeVaultRequest()
        {
            VaultName = vaultName
        };

        await _amazonGlacier.DescribeVaultAsync(describeVaultRequest);

    }
    catch (ResourceNotFoundException)
    {

        var createVaultRequest = new CreateVaultRequest()
        {
            VaultName = vaultName
        };

        var createVaultResponse = await _amazonGlacier.CreateVaultAsync(createVaultRequest);

        if (createVaultResponse.HttpStatusCode == System.Net.HttpStatusCode.OK)
            return Results.Ok("Vault created");
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Vault not created : {ex.Message}");

    }


    return Results.Ok("Vault  created");

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
    DescribeVaultResponse describeVaultResponse = default;

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
        return Results.NotFound("Vault not exists");
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Vault not created : {ex.Message}");

    }

    return Results.Ok(describeVaultResponse);



});


app.MapPost("/upload-archive", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName,
    [FromForm] IFormFile file
    ) =>
{

    try
    {
        var describeVaultRequest = new DescribeVaultRequest()
        {
            VaultName = vaultName
        };

        await _amazonGlacier.DescribeVaultAsync(describeVaultRequest);

    }
    catch (ResourceNotFoundException)
    {
        return Results.NotFound("Vault not exists");
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"not upload file to vault : {ex.Message}");

    }


    var uploadArchieveRequest = new UploadArchiveRequest()
    {
        VaultName = vaultName,
        Body = file.OpenReadStream(),
        ArchiveDescription = $"filename : {file.Name} + {file.FileName}",
        Checksum = TreeHashGenerator.CalculateTreeHash(file.OpenReadStream())
    };


    var uploadArchiveResponse = await _amazonGlacier.UploadArchiveAsync(uploadArchieveRequest);


    return Results.Ok(uploadArchiveResponse);


}).DisableAntiforgery();

app.MapPost("/initate-job", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName,
    [FromQuery] string archiveId) =>
{

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



    var initiateJobResponse = await _amazonGlacier.InitiateJobAsync(initiateJobRequest);

    return Results.Ok(initiateJobResponse);

});

app.MapGet("list-jobs", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string vaultName) =>
{
    var listJobRequest = new ListJobsRequest()
    {
        Limit = 10,
        VaultName = vaultName
    };

    return await _amazonGlacier.ListJobsAsync(listJobRequest);

});

app.MapGet("get-job-description", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string jobId,
    [FromQuery] string vaultName) =>
{

    try
    {
        var describeVaultRequest = new DescribeVaultRequest()
        {
            VaultName = vaultName
        };
        await _amazonGlacier.DescribeVaultAsync(describeVaultRequest);

    }
    catch (ResourceNotFoundException)
    {
        return Results.NotFound("vault not found");
    }
    catch (Exception ex)
    {
        return Results.NotFound(ex.Message);
    }


    try
    {

        var describeJobRequest = new DescribeJobRequest()
        {
            JobId = jobId,
            VaultName = vaultName

        };

        var describeJobResponse = await _amazonGlacier.DescribeJobAsync(describeJobRequest);

        return Results.Ok(describeJobResponse);
    }
    catch (ResourceNotFoundException)
    {
        return Results.NotFound("job not found");

    }
    catch (Exception ex)
    {
        return Results.BadRequest($"job not found: {ex.Message}");
    }




});

app.MapGet("get-archive", async (
    [FromServices] IAmazonGlacier _amazonGlacier,
    [FromQuery] string jobId,
    [FromQuery] string vaultName) =>
{
    try
    {
        var describeVaultRequest = new DescribeVaultRequest()
        {
            VaultName = vaultName
        };
        await _amazonGlacier.DescribeVaultAsync(describeVaultRequest);

    }
    catch (ResourceNotFoundException)
    {
        return Results.NotFound("vault not found");
    }
    catch (Exception ex)
    {
        return Results.NotFound(ex.Message);
    }

    DescribeJobResponse describeJobResponse = default;

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
        return Results.NotFound("job not found");

    }
    catch (Exception ex)
    {
        return Results.BadRequest($"job not found: {ex.Message}");
    }

    if (!describeJobResponse.Completed)
        return Results.BadRequest("job not completed");


    var getJobOutputRequest = new GetJobOutputRequest()
    {
        JobId = jobId,
        VaultName = vaultName
    };

    var getJobOutputResponse = await _amazonGlacier.GetJobOutputAsync(getJobOutputRequest);


    return Results.Ok(getJobOutputResponse);


});

internal record FileInfoModel(string fileName, string fileType);

internal record VaultIsExistsModel(
    string vaultName,
    string errorMessage,
    DescribeVaultResponse describeVaultResponse);

internal record JobIsExistsModel(
    string vaultName,
    string errorMessage,
    DescribeJobResponse describeJobResponse);



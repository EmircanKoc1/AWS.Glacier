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


app.Run();


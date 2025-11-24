using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<IResourceWithConnectionString> tg = builder.AddConnectionString("Telegram");

IResourceBuilder<PostgresServerResource> db = builder.AddPostgres("RuItUnion-FeedbackBot-Database")
    .WithDataVolume("RuItUnion-FeedbackBot-Database-Data")
    .WithImageTag("17-alpine");

builder.AddProject<RuItUnion_FeedbackBot>("RuItUnion-FeedbackBot")
    .WithReference(db).WithReference(tg)
    .WithHttpHealthCheck("/health");

await builder.Build().RunAsync();
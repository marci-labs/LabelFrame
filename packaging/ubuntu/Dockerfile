# LabelFrame Server（Ubuntu 容器，framework-dependent；迭代 19）
# 构建：docker build -f packaging/ubuntu/Dockerfile -t labelframe-server artifacts/server-linux/linux-x64
# 运行：docker run -d --name labelframe-server -p 53961:53961 -v labelframe-data:/var/lib/labelframe/server labelframe-server
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . /app
ENV LABELFRAME_SERVER_LISTEN=http://0.0.0.0:53961
ENV LABELFRAME_SERVER_DB=/var/lib/labelframe/server/server.db
ENV LABELFRAME_SERVER_TEMPLATES_DB=/var/lib/labelframe/server/templates.db
ENV LABELFRAME_SERVER_LOGS_DB=/var/lib/labelframe/server/logs.db
EXPOSE 53961
ENTRYPOINT ["/app/LabelFrame.Server"]
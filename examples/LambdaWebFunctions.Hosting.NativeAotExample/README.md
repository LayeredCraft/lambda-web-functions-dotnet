# NativeAOT example

This is the deployable proof of the package contract. Publish it for `linux-arm64`, put the
published `server` executable and `index.js` at the root of a Lambda Web Function zip, and deploy.

```bash
dotnet publish --configuration Release --runtime linux-arm64 --self-contained true \
  -p:PublishAot=true -o publish
```

`GET /health` returns `OK`.

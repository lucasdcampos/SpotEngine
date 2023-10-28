<img src="assets/img/spot/spot-logo.png" width="480px">

# Spot Game Engine

Welcome to the Spot Game Engine, a work-in-progress game engine built entirely in C#.

**Please Note:** There are no publicly available releases of the engine at this time. We are actively developing it, and we'll share more updates as we make progress.

## Getting Started

While the engine is under development and no releases are available, you can explore the source code and contribute if you wish. Here's how you can get started:

```sh
git clone https://github.com/lukazof/SpotEngine.git
cd SpotEngine
```

Now you can Build it from source:

```sh
.\build.ps1
```

This is Windows only, in the future a *build.sh* can be created, which runs on Linux and macOS

You can test if it worked by using `dotnet run --project tests/TestApp/TestApp.csproj` (The default app used for testing purposes should now open)

## How to Contribute

We welcome contributions to the Spot Game Engine! If you'd like to contribute, please follow these steps:

1. Fork the repository.
2. Create a new branch for your feature or bug fix.
3. Make your changes and ensure they follow coding standards.
4. Submit a Pull Request (PR) with your changes.
5. Your PR will be reviewed, and upon approval, it may be merged.

For major changes or significant contributions, please discuss your ideas in advance by opening an issue.

## License

The Spot Game Engine is open source and available under the MIT License. See [LICENSE](LICENSE.md) for more details.

---

### Notes from the Author
I'm developing the engine alone (you can contribute if you want), and mainly for study purposes and also out of curiosity. 
I don't intend to create the most complex engine in the world, because that requires a giant team and years of work.

My goal is to be able to create simple games using the engine, and in the future I might think about expanding it, who knows.

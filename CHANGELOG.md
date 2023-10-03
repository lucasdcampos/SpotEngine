# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)

# 0.0.2 - 2023-10-02

## Added

New Graphical API was added: SFML.Net

<ul>
    <li>SFMLRenderer.cs</li>
</ul>

## Removed

Removed DrawSquare function from IGraphicRenderer (the graphics interface), but
you can still use it in the specified renderer

# 0.0.1 - 2023-09-20

This is the first development version, there isn't much to say.

## Added

SharpGL was added as a test

## Changed

### New project structure
Files are now organized by the following structure:

<ul>
    <li>assets</li>
    <li>docs</li>
    <li>editor</li>
    <li>engine</li>
    <li>test</li>
    <li>LICENSE.md</li>
    <li>README.md</li>
    <li>CHANGELOG.md</li>
    <li>.gitignore</li>
    <li>spot.sln</li>
</ul>
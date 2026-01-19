<a name="readme-top"></a>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stars][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![Apache-2.0 License][license-shield]][license-url]
[![LinkedIn][linkedin-shield]][linkedin-url]

<br />
<div align="center">
  <h3 align="center">jihub</h3>

  <p align="center">
    Command Line Tool to help exporting Issues and Attachments from Jira and import them as GitHub Issues
    <br />
    Forked from : <a href="https://github.com/phil91/jihub">https://github.com/phil91/jihub</a>
  </p>
</div>


<details>
  <summary>Table of Contents</summary>

  * [About The Project](#about-the-project)
  * [Getting Started](#getting-started)
    * [Prerequisites](#prerequisites)
    * [Setup](#setup)
  * [Usage](#usage)
    * [Create you Jira search query](#create-you-jira-search-query)
    * [Execute Jihub](#execute-jihub)
  * [Known Limitations](#known-limitations)
  * [Contributing](#contributing)
  * [License](#license)
</details>

## About The Project

`Jihub` is a sophisticated tool designed to streamline the process of importing Jira issues into GitHub based on specific search queries. With `Jihub`, you can seamlessly convert Jira issues into GitHub Issues, ensuring a smooth transition and preserving vital information such as labels, fix versions, and attachments.

Additionally, `Jihub` offers an optional feature to export Jira attachments directly to a designated GitHub repository, further enhancing collaboration and consolidating project resources.

**API Compatibility**: As of January 2026, `Jihub` uses Jira REST API v3 and is compatible with modern Jira Cloud instances. The project targets .NET 8.0 for optimal performance and long-term support.

For detailed instructions on how to utilize `Jihub` effectively, please refer to the [Usage](#usage) section. Experience the convenience and efficiency of effortlessly migrating your Jira issues to GitHub with `Jihub`.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Getting Started

To start using `Jihub`, please follow the steps below.

### Prerequisites

Before executing `Jihub`, make sure you have the following prerequisites:

- A GitHub Personal Access Token (PAT) for authentication.
    - **Required Permissions**:
        - **Repository**:
            - Contents (Read & Write)
            - Issues (Read & Write)
            - Metadata (Read-Only)
            - Pull Requests (Read & Write)
        - **Organizations**:
            - Members (Read-Only)
            - Projects (Read & Write)
- Write access to the github repository you want to import the issues to
- Valid login credentials for a publicly accessible Jira instance.
- OPTIONAL: If you want to export the jira attachments you need write access to the repository you want to import the attachments to.

### Setup

To setup `Jihub`, just follow these steps:

1. Download the release package of your choice.
2. Within the package, locate the specific zip file for your operating system. For example, if you're using macOS, select `jihub-osx64.zip`.
3. Alternatively, you can download the latest build artifact from the [Actions](https://github.com/Phil91/jihub/actions) section. On the left side, select the Build action and download the artifact that matches your operating system.
4. Unzip jihub and navigate into the unziped folder.
5. Open the appsettings.json with an editor of your choice and fill the following fields

| Configuration      | Key                  | Value          |
|--------------------|----------------------|----------------|
| Jira               | JiraInstanceUrl      | The url of your jira instance, e.g. https://example-jira.com/                |
|                    | JiraUser             | Your jira username                |
|                    | JiraPassword         | Your jira password               |
| GitHub             | Token                | Your generated GitHub PAT               |
| Parsers:Jira       | EmailMappings        | Mapping between JiraUsername and Github Name, should be in the following format: { "JiraUsername": "Beta tester", "GithubName": "Phil91" }               |
| Parsers:Jira       | DescriptionTemplate  | Template for the Github description. Valid values currently: Description, Components, Sprints, FixVersions, StoryPoints, Attachments please make sure that those values are listed within {{}}               |


By following these steps, you'll be able to use `Jihub` for importing Jira issues to GitHub.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Usage

### Create you Jira search query

In your Jira Instance, navigate to `/issues`, for example: https://your-jira-instance.com/issues.
You can filter for the issues of your needs. After your search result is as wanted, click the Advanced Link right next to the serach button to be able to see your jql query. Copy the query and URL encode it (there are plenty of sites doing it on the fly online).

### Execute Jihub

Once you completed the setup open a terminal and navigate to your jihub folder. To run the import simply run:

```shell
dotnet jihub.Worker.dll
```


| Parameter          | Short Name | Long Name       | Required | Default | Description                                                                                |
|--------------------|------------|-----------------|----------|---------|--------------------------------------------------------------------------------------------|
| Repo               | -r         | --repo          | Yes      |         | Name of the GitHub repository                                                             |
| Owner              | -o         | --owner         | Yes      |         | Username of the GitHub user or organization that hosts the project                         |
| SearchQuery        | -q         | --query         | Yes      |         | The search query to filter Jira issues, must be url encoded                                                    |
| MaxResults         | -m         | --max-results   | No       | 1000    | The maximum number of Jira results to retrieve                                             |
| Link               | -l         | --link          | No       | false   | If set, all external resources such as images will be referred to as a link in the description |
| ContentLink        | -c         | --content-link  | No       | false   | If set, all external resources such as images will be linked as content in the description |
| Export             | -e         | --export        | No       | false   | If set, all external resources such as images will be exported to the given repository     |
| UploadRepo         | -u         | --upload-repo   | No       |         | The repository where the Jira assets will be uploaded                                      |
| ImportOwner        | -i         | --import-owner  | No       |         | The owner of the repository where the Jira assets should be uploaded                       |
| ImportPath         | -p         | --import-path   | No       |         | The path of the directory where the Jira assets should be uploaded                       |
| Branch             | -b         | --branch        | No       | main    | The branch of the repository common for attachments                                      |
| LinkChildren       |            | --linkChildren  | No       | false   | If set, the children will be linked in the body                                            |
| LinkRelated        |            | --linkRelated   | No       | false   | If set, the related issues will be linked in the comments                                 |
| ProjectNumber      |            | --project-number| No       |         | Number of the GitHub Project (v2) to add issues to                                        |
| ProjectOwner       |            | --project-owner | No       |         | Owner of the GitHub Project (if different from repo owner)                                |
| AdditionalLabel    |            | --additional-label| No     |         | A static label to add to all imported issues                                              |
| LinkPrs            |            | --link-prs      | No       | false   | If set, tries to find PR links (remote links & dev panel) and link them natively to the GitHub issue |

Please note that the "Required" column indicates whether a parameter is mandatory or not, and the "Default" column shows the default value if not specified.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Known Limitations

Due to GitHub Api limitations the import of the issues to GitHub will pause for 20 seconds after 10 created issues before continuing with the next 10.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".
Don't forget to give the project a star! Thanks again!

For further information on how to contribute, take a look at [Contributing](https://www.github.com/phil91/jihub/CONTRIBUTING.md)

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## License

Distributed under the Apache-2.0 License. See [LICENSE](https://www.github.com/phil91/jihub/LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/phil91/jihub.svg?style=for-the-badge
[contributors-url]: https://github.com/phil91/jihub/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/phil91/jihub.svg?style=for-the-badge
[forks-url]: https://github.com/phil91/jihub/network/members
[stars-shield]: https://img.shields.io/github/stars/phil91/jihub.svg?style=for-the-badge
[stars-url]: https://github.com/phil91/jihub/stargazers
[issues-shield]: https://img.shields.io/github/issues/phil91/jihub.svg?style=for-the-badge
[issues-url]: https://github.com/phil91/jihub/issues
[license-shield]: https://img.shields.io/github/license/phil91/jihub.svg?style=for-the-badge
[license-url]: https://github.com/phil91/jihub/blob/master/LICENSE
[linkedin-shield]: https://img.shields.io/badge/-LinkedIn-black.svg?style=for-the-badge&logo=linkedin&colorB=555
[linkedin-url]: https://linkedin.com/in/phils91

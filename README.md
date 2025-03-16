# Artifact for Paper "Metamorph: Synthesizing Large Objects from Dafny Specifications", OOPSLA 2025

# Table of Contents:

- [Artifact for Paper "Metamorph: Synthesizing Large Objects from Dafny Specifications", OOPSLA 2025](#artifact-for-paper-metamorph-synthesizing-large-objects-from-dafny-specifications-oopsla-2025)
- [Table of Contents:](#table-of-contents)
  - [1. Introduction](#1-introduction)
  - [2. Hardware Dependencies](#2-hardware-dependencies)
  - [3. Getting Started Guide](#3-getting-started-guide)
  - [4. Step-by-Step Instructions](#4-step-by-step-instructions)
    - [4.1. Large Object Synthesis (Figures 8,10)](#41-large-object-synthesis-figures-810)
    - [4.2. DTest Case Study (Section 6.6)](#42-dtest-case-study-section-66)
    - [4.3. EVM Case Study (Table 1)](#43-evm-case-study-table-1)
    - [4.4. Running Time Analysis (Figure 9)](#44-running-time-analysis-figure-9)
    - [4.5. (Optional) Native Installation Instructions](#45-optional-native-installation-instructions)
  - [5. Reusability Guide](#5-reusability-guide)
    - [5.1. Artifact File Index](#51-artifact-file-index)
    - [5.2. Metamorph's CLI](#52-metamorphs-cli)
    - [5.3. Benchmark Structure](#53-benchmark-structure)

## 1. Introduction

This artifact presents Metamorph, a tool for synthesis of Dafny code. The 
artifact contains the source code for Metamorph and all the benchmarks discussed
in the accompanying paper. The purpose of the artifact is to allow reproduction 
of all experiments outlined in the paper. 

The key results that the artifact aims to demonstrate are: 
- Metamorph scales approximately linearly with the size of the synthesis problem
(Figure 8 in the paper).
- Metamorph can be used in conjunction with DTest (Section 6.6 of the paper).
- Metamorph can be used in non-trivial scenarios, such as generation of bytecode 
for a simple virtual machine (Table 1 of the paper)

## 2. Hardware Dependencies

A machine with 8GB+ of RAM running MacOS or Ubuntu should be sufficient to 
validate the key findings of the paper and evaluate the artifact. Selected few 
synthesis problems may require more RAM and/or installing the artifact 
natively instead of running it in a Docker container.  
(see the [Step-by-Step Instructions](#4-step-by-step-instructions)).

We have tested the artifact using Docker on the following machines:
- An M3 Mac machine running MacOS Sonoma 14.7 with 48GB of memory (of which Docker was allowed to use 8GB)
- An AMD64 Dell machine running Ubuntu 20.04 with 32GB of memory.

We have additionally installed the artifact natively on the machines above as well. 
Finally, we produced the results for the paper using a 384GB RAM Ubuntu server.


## 3. Getting Started Guide

To set up the artifact, please follow these instructions:

- **Step 1**. Install Docker Engine. See [https://docs.docker.com/engine/install/](https://docsdocker.com/engine/install/) 
for instructions. Please check in Docker settings that the Memory Limit is set to 8GB 
or more. If you are using Ubuntu, Docker should automatically allow itself to use as 
much memory as your machine has available. If you are running Docker Desktop, we 
recommend increasing the Memory Limit and Swap space in Docker Settings if your 
host machine allows it, although 8GB should be sufficient for reproducing key findings.

- **Step 2**. Make sure Docker daemon is running (e.g., by launching Docker app).
Next, open the Terminal and navigate to the root directory of this artifact. 
To build the Docker container, run:
```sh
docker build -t metamorph .
```
You only need to run the above command once.

- **Step 3**. To obtain an interactive shell in the container, run:
```sh
docker run -it --rm -v "$(pwd):/metamorph" metamorph bash
```

- **Step 4**. In Docker shell, enter the root directory of the artifact 
(remember to repeat this step whenever you relaunch a new shell):

```sh
cd metamorph
```

- **Step 5**. To build Metamorph, run one of the following two commands, 
depending on your machine's processor. You do not need to build Metamorph 
again if you relaunch the shell.

```sh
make docker-intel
make docker-arm
```

- **Step 6**. To verify your installation, run Metamorph like so:
```sh
dotnet Metamorph/Binaries/Metamorph.dll --input Benchmarks/SocialNetwork/Problems/Problem01.dfy
```

If your installation was successful, you will see the following output:

```dafny
static method solution() returns (result:SocialNetwork)
ensures fresh(result) && Goal(result)
{
result := new SocialNetwork();
result.AddUser(0);
}

```
 
## 4. Step-by-Step Instructions

In what follows, we describe how to reproduce all the figures and, by extension, 
the findings of the accompanying paper. Please note that the scripts discussed 
below are not designed to run in parallel (so please run them one at a time).
Also note that all the figures produced while running Docker shell will also be
saved inside the artifact root folder on your host machine (so you can open and
view them from outside Docker).

### 4.1. Large Object Synthesis (Figures 8,10)

The `scripts/evaluate.py` script handles the creation of the plots shown in Figures 8 
and 10 of the paper. For the paper, we have used a time limit of 4 hours per 
synthesis problem and, therefore, complete reproduction of results may take 
multiple days. **Instead, we suggest specifying a timeout of 5 minutes per 
problem, which will generate plots much faster and is still sufficient to 
observe the main findings of the paper.** If you wish to replicate the results 
completely, we suggest installing the artifact natively on a machine that has 
32GB+ of RAM (read [this](#45-optional-native-installation-instructions) 
for more details).

To generate a plot for a given benchmark with a time limit of 5 minutes 
(300 seconds), run:
```sh
python3 scripts/evaluate.py -benchmark BENCHMARK -timeLimit 300
```

Acceptable arguments for the `BENCHMARK` parameter are `FreezableArray`,
`BinaryTree`, `SocialNetwork`, `Firewall`, `DoublyLinkedList`, and `Queue` 
(to replicate plots in Figure 8 in the paper) as well as `FreezableArrayMod` 
and `SocialNetworkMod` (to replicate Figure 10). You may also specify `Figure8` 
or `Figure10` as a benchmark, in which case the script will generate all the 
plots for the given figure in order. Note that the script will cache results 
in the `cache/Results.csv` file, so it will not rerun Metamorh on the same problem 
again unless you explicitly clear the cache. If you wish to reset the cache, 
you can run the script with the `--clearCache` flag. You can also copy the contents 
of `cache/Results_OLD.csv` into `cache/Results.csv`, which will recreate the exact
figures from the paper since `cache/Results_OLD.csv` stores the results of the
experiments used for the paper.

To quickly convince yourself of the key results of the paper, we recommend 
generating Figures 8.c (BinaryTree), and 10.a (FreezableArrayMod), since the 
corresponding benchmarks are the least resource-heavy. The script will 
generate two PDF files in the base directory with plots that should 
be similar to those in the paper except for a lower time limit cut-off. 
In particular, you should see that Metamorph outperforms the baseline 
in Figure 8.c and the piecewise distance metric outperforms the 
greedy metric in Figure 10.a. You can generate these figures using the 
following commands:

```sh
python3 scripts/evaluate.py -benchmark BinaryTree -timeLimit 300
python3 scripts/evaluate.py -benchmark FreezableArrayMod -timeLimit 300
```

Each command should take about half an hour to execute. The results from 
these experiments form what we refer to as a "representative portion" 
(of the results) in our original Data Availability Statement.

Below, we list the (approximate) time that it takes to generate each plot. 

| Benchmark           | With 5 min limit | With 4 hour limit |
|---------------------|------------------|-------------------|
| FreezableArray      | 63 min           | 816 min           |
| BinaryTree          | 36 min           | 250 min           |
| SocialNetwork       | 55 min           | 967 min           |
| Firewall            | 85 min           | 660 min           |
| DoublyLinkedList    | 41 min           | 1395 min          |
| Queue               | 62 min           | 1328 min          |
| FreezableArrayMod   | 22 min           | 22 min            |
| SocialNetworkMod    | 31 min           | 832 min           |
| **Figure 8 Total**  | 6 hours          | 90 hours          |
| **Figure 10 Total** | 1 hour           | 12 hours          |


### 4.2. DTest Case Study (Section 6.6)

This step should take approximately 5-10 minutes.

There are two benchmarks on which we have tested integration between Metamorph 
and DTest, Dafny's automated test generation toolkit. These benchmarks are 
`SocialNetworkTestGeneration` and `CMTL`. The `CMTL` benchmarks is a portion of 
the AWS Cryptographic Material Providers Library, as described in the paper (the
library is licensed under Apache 2.0, license included with the artifact). The 
original files for this portion of the library can be found on official AWS 
GitHub [here](https://github.com/aws/aws-cryptographic-material-providers-library/blob/1c08aa75bed81e031fb102ddb8ea803c9ce97296/AwsCryptographyPrimitives/src/HKDF/HMAC.dfy) 
and [here](https://github.com/aws/aws-cryptographic-material-providers-library/blob/1c08aa75bed81e031fb102ddb8ea803c9ce97296/AwsCryptographyPrimitives/src/HKDF/HKDF.dfy). 

To test Metamorph's integration with DTest, please run the commands below.
The second numerical argument in these commands is provided to DTest and 
specifies the maximum length of sequence objects it is allowed to generate 
(so not relevant to Metamorph). If you are running the commands natively
(as opposed to using Docker), you may need to prefix then with `sudo`. 
The first command should take 1-2 minutes, 
and the second command should take 5-10 minutes:

```sh
scripts/./generate_tests_with_metamorph.sh Benchmarks/CMPL/HKDF.dfy 80
scripts/./generate_tests_with_metamorph.sh Benchmarks/SocialNetworkTestGeneration/Definitions.dfy 4
```

If these commands succeed without errors, this means that DTest+Metamorph have 
successfully generated and executed tests for the specified APIs. The command 
should also report how much time each step of the process took (test generation, 
pretraining, and synthesis). Please note that these numbers can vary 
from system to system and so may differ from those reported in 
Section 6.6 of the paper. The key result is the ability to generate the tests. 

If you wish to inspect the output in more detail and/or need to debug the 
process, you can check the following:

- A file named `DefinitionTests.dfy` will appear in each benchmark's 
subdirectory. This file is generated by DTest. You can confirm that this step 
succeeded by searching for `:synthesize` annotations in the file -- these are 
synthesis directives for Metamorph.

- A file named `DefinitionSynthesized.dfy` will appears in each benchmark's 
subdirectory. This file is created by repeatedly calling Metamorph on 
`DefinitionTests.dfy` and substituting synthesis solutions as bodies for 
methods, whose names start with `GenerateClassTypePredicate`. You can confirm 
this step succeeded by searching for a method with such a name and confirming 
that it has a body.

- A file named `DefinitionsResults.txt` will appear in each benchmark's 
subdirectory. This file is created by compiling `DefinitionSynthesized.dfy` and 
executing the tests. The file will contain compilation warning at the top -- these 
are normal and are due to compiling C# code generated by the Dafny to C# compiler. 
You can confirm that the tests succeeded by checking that 
string "FAILED" is not part of the output. Additionally, for each `print` 
statement in the original `Definitions.dfy` file for the 
`SocialNetworkTestGeneration` benchmark, there will be a corresponding line in 
the `DefinitionsResults.txt` file (which just means that a test executed the 
corresponding print statement). For example, the output should contain strings 
"Cannot recommend", "Recommending", and "No friends of friends".


### 4.3. EVM Case Study (Table 1)

To reproduce the results of the EVM case study, please run the command 
below (which should take around 15 to 30 minutes).

```sh
python3 scripts/evaluate.py -benchmark EVM -timeLimit 600
```

The script will create a file called `Table1.csv`, which will have the same 
format as Table 1 in the paper. The exact numbers may differ from the paper 
but the key findings are that:
- Metamorph can synthesize all problems using either of the two distance metrics.
- The StackOverflow problem takes most time to synthesize but Metamorph can still
consistently solve it (despite the solution consisting of over 10 method calls).

### 4.4. Running Time Analysis (Figure 9)

This step should take a minute or less.

To reproduce Figure 9 (`Figure9.pdf`), please run:
```sh
python3 scripts/running_time_analysis.py -logs cache/logs_OLD
```

The script produces the Figure by analyzing the log files generated by 
Metamorph. The `cache/logs_OLD` directory contains all such logs produced when we
first performed all the experiments for the paper. You can also generate this 
figure from the logs you generated when following the steps above (assuming you
performed each step exactly once), if you replace `cache/logs_OLD` with `cache/logs`
in the command. Depending on which subset of the experiments you run and on 
the time limit you set (see [4.1](#41-large-object-synthesis-figures-810)), 
the figure you get might differ from the one in the paper. 
None of the key conclusions we make in the paper depend on the data in Figure 9.

### 4.5. (Optional) Native Installation Instructions

If you wish to install the artifact natively on your machine (as opposed to 
using Docker), please follow the instructions below. **We strongly recommend 
that you follow installation instructions from the 
[getting started guide](#getting-started-guide) instead since your personal 
machine's configuration may interfere with the artifact.** However, if you wish   
to run Metamorph on some of the more resource-intensive synthesis problems (e.g.
using a 4 hour time limit when reproducing [Figures 8 and 10](#41-large-object-synthesis-figures-810)), you may need to install the artifact 
natively. In this case, you also need a machine that has 32GB+ of RAM and at 
least an equal amount of swap space (unfortunately, Metamorph uses some of the 
internal features of the Boogie language, which are not optimized for C# garbage
collector). If you still wish to install the artifact natively, please follow 
these instructions:

- Install dotnet (must have both .NET 6.0 SDK and .NET 7.0 SDK) for your system.
You can download dotnet-6.0 from [https://dotnet.microsoft.com/en-us/download/dotnet/6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) and dotnet-7.0 from [https://dotnet.microsoft.com/en-us/download/dotnet/7.0](https://dotnet.microsoft.com/en-us/download/dotnet/7.0).

- Install python3, if not already on your machine. 

- Run `pip3 install matplotlib numpy psutil` to install the python packages 
necessary for reproducing the Figures in the paper. We recommend using the
latest stable package versions. When producing the results for the paper,
we have used `numpy=1.26.4`, `psutil=5.9.0`, and `matplotlib=3.8.4`.

- Depending on your system, run `make ubuntu`, `make mac`, or `make mac-arm`.

## 5. Reusability Guide

In addition to validating the result of the original study, this artifact should
allow both testing Metamorph on new benchmarks and testing other tools on 
Metamorph's benchmarks. To facilitate reusability, we document this artifact's structure, 
list Metamorph's command-line options and describe the requirements for future benchmarks.

### 5.1. Artifact File Index

This artifact contains the following files:

- `Metamorph` directory contains Metamorph's source code as well as the 
source code of the Dafny fork that Metamorph uses.
- `Behchmarks` directory contains the benchmarks on which Metamorph has been 
tested. See [below](#53-benchmark-structure) for an overview of what goes
into each benchmark.
- `scripts` contains python and bash scripts used to automated evaluation.
- `cache` saves temporary data that is computed during Metamorph's evaluation.
In particular, `cache/Results_OLD.csv` and `cache/logs_OLD.csv` store the 
evaluation results and the logs on which the figures in the paper are based. 
`cache/Results.csv` and `cache/logs` are where the new results and logs will 
be saved by default. `cache/pretraining` stores the data Metamorph computes 
in the pretraining phase.
- `LICENSE.txt` describes how the different constituent parts of this artifact
are licensed
- `README.md` is this file.
- `Makefile` allows building Metamorph (but is agnostic of the evaluation scripts)
- `Dockerfile` allows building Metamorph and the evaluation materials on most UNIX 
systmes using Docker.

### 5.2. Metamorph's CLI

Metamorph supports the following command line options:

- `--input [FILENAME]`: a Dafny file with the synthesis problem. The file must 
have at least one predicate annotated with the `{:synthesize}` attribute unless
Metamorph is run with the `-pretrain` flag.

- `--goal [STRING]`:  the name of the synthesis goal to process. This is 
necessary if you have several `{:synthesize}` annotated predicates in your input
file. For example, if your input file has one predicate annotated with 
`{:synthesize "A"}` and another one annotated with `{:synthesize "B"}`, you must
 run Metamorph with either `-goal A` or `-goal B`.

- `--noDistanceMetric`: runs Metamorph without any of the distance metrics 
discussed in Section 4 of the paper.

- `--greedy`: Use the greedy distance metric instead of the default piecewise 
distance metric.

- `--pretrain [DIRECTORY]`: this flag activates the pretraining mode (see 
Section 4.3 of the paper). In this mode, Metamorph extracts symbolic constraints
relevant to a particular API and saves information about possible interactions 
between constraints and methods to the specified directory. This information can 
be loaded on subsequent runs with the `--loadPretrained` flag. When 
`--pretrain` flag is used, the input file does not need to contain any 
synthesis problems, only the API itself.

- `--timeLimit [SECONDS]`: preemptively terminate the synthesis after the 
specified number of seconds has elapsed.

- `--loadPretrained [DIRECTORY]` : load pre-trained data from the specified 
directory.

### 5.3. Benchmark Structure

Most Metamorph's benchmarks are organized in the following way: 

- The `Benchmarks/[Name]/Definitions.dfy` file defines the API with methods that
Metamorph can use during synthesis. All such methods must be instance methods of
some class and they must be annotated with `{:use}` attribute. Additionally, we 
recommend that all the code in the file is enclosed in a top-level module. For 
example:
```dafny
module Definitions {
  class SomeType {
    var field:int;
    // must include a constructor for each class:
    constructor() {}; 
    method {:use} SetField(i:int) 
      modifies this
      ensures field == i // API methods need to have specifications
    { this.field := i }
  }
}
```

- The `Benchmarks/[Name]/Problems/` directory contains a number of `.dfy` files 
with the synthesis problems. A typical synthesis problem file would contain a 
single predicate annotated with the `{:synthesize}` attribute, which returns 
true if its argument satisfies the necessary criteria. For example:
```dafny
include "../Definitions.dfy" // include the API
module Problem {
  import opened Definitions  // import the API
  predicate {:synthesize} Goal(result:SomeType)
    reads result
  { result.field = 3 /* example synthesis goal */ }
}
```

- The `Benchmarks/[Name]/Problems/BaselineTemplate.dfy` file is not used by Metamorph 
itself but by the baseline synthesis solution against which we test Metamorph. 
The file defines a DSL where each instruction is a call to one of the API methods 
available during synthesis. This allows using Dafny's built in test generation as a
synthesis-tool (controlled via `baseline.py`).

- To test Metamorph on a benchmark, you would typically first pretrain Metamorph
on the relevant API:
```sh
dotnet Metamorph/Binaries/Metamorph.dll --input Benchmarks/[NAME]/Definitions.dfy --pretrain Benchmarks/[NAME]/cache/
```

- After which you can use the data obtained during pretraining to run Metamorph 
on a particular synthesis problem
```sh
dotnet Metamorph/Binaries/Metamorph.dll --input Benchmarks/[NAME]/Problems/Problem01.dfy --loadPretrained Benchmarks/[NAME]/cache/
```

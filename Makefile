mac:
	cd Metamorph/dafny && make exe && make z3-mac
	dotnet build Metamorph/Metamorph.sln
	echo 'export PATH="${PATH}:/Metamorph/Binaries/z3/bin"' >> ~/.zshrc
	echo 'export PATH="${PATH}:/Metamorph/Binaries/z3/bin"' >> ~/.bashrc
	echo 'export PATH="${PATH}:/Metamorph/Binaries/z3/bin"' >> ~/.bash_profile
	export PATH="${PATH}:/Metamorph/Binaries/z3/bin"

mac-arm:
	cd Metamorph/dafny && make exe && make z3-mac-arm
	dotnet build Metamorph/Metamorph.sln
	echo 'export PATH="${PATH}:/Metamorph/Binaries/z3/bin"' >> ~/.zshrc
	export PATH="${PATH}:/Metamorph/Binaries/z3/bin"

docker-intel:
	cd Metamorph/dafny && make exe && make z3-ubuntu
	dotnet build Metamorph/Metamorph.sln
	echo 'export PATH="${PATH}:/Metamorph/Binaries/z3/bin"' >> ~/.bashrc
	export PATH="${PATH}:/Metamorph/Binaries/z3/bin"

docker-arm:
	cd Metamorph/dafny && make exe && make z3-ubuntu-arm
	dotnet build Metamorph/Metamorph.sln
	echo 'export PATH="${PATH}:/Metamorph/Binaries/z3/bin"' >> ~/.bashrc
	export PATH="${PATH}:/Metamorph/Binaries/z3/bin"
	
ubuntu: docker-intel

ubuntu-arm: docker-arm

test:
	dotnet Metamorph/Binaries/Metamorph.dll -i Benchmarks/SocialNetwork/Problems/Problem02.dfy --timeLimit 360
	dotnet Metamorph/Binaries/Metamorph.dll -i Benchmarks/DoublyLinkedList/Problems/Problem01.dfy  --timeLimit 240
	dotnet Metamorph/Binaries/Metamorph.dll -i Benchmarks/BinaryTree/Problems/Problem12.dfy --timeLimit 240
	dotnet Metamorph/Binaries/Metamorph.dll -i Benchmarks/FreezableArray/Problems/Problem07.dfy --timeLimit 240

rebuild:
	dotnet clean Metamorph/Metamorph.sln
	dotnet clean Metamorph/dafny/Source/Dafny.sln
	dotnet restore Metamorph/dafny/Source/Dafny.sln
	dotnet build Metamorph/dafny/Source/Dafny.sln
	dotnet restore Metamorph/Metamorph.sln
	dotnet build Metamorph/Metamorph.sln

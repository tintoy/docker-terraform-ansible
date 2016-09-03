FROM ddresearch/terraform-provider-ddcloud:v1.0

RUN apk add --update bash python py-pip
RUN pip install --upgrade pip

RUN apk add ansible sshpass

COPY home /root
WORKDIR /root

# Don't prompt for input.
ENV TF_INPUT=0
ENV TF_VARIABLES_FILE=/root/state/tfvars.json

# Maximum degree of parallelism for Terraform
ENV MAX_TF_PARALLELISM=10

RUN mkdir -p /root/state
VOLUME /root/state

ENTRYPOINT ["/bin/bash", "/root/deploy.sh"]

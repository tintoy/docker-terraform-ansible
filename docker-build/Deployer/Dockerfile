FROM ddresearch/terraform-provider-ddcloud:v1.0

RUN apk add --update bash python py-pip
RUN pip install --upgrade pip

RUN apk add ansible

COPY home /root
WORKDIR /root

# Don't prompt for input.
ENV TF_INPUT=0
ENV TF_VARIABLES_FILE=/root/state/terraform.tfvars

RUN mkdir -p /root/state
VOLUME /root/state

CMD ["/bin/bash", "deploy.sh"]
